using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Dashboard;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class DashboardService : IDashboardService
{
    // Status id contract (matches seeded TaskStatuses): 1 NotStarted, 2 InProgress, 3 ForReview, 4 Completed.
    private const int StatusIdNotStarted = 1;
    private const int StatusIdInProgress = 2;
    private const int StatusIdForReview = 3;
    private const int StatusIdCompleted = 4;

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;

    public DashboardService(AccountDbContext context)
    {
        _context = context;
    }

    // ----------------------------------------------------------------------
    // Summary cards
    // ----------------------------------------------------------------------

    public async Task<DashboardSummaryResponse> GetSummaryAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        var requester = await GetRequesterAsync(requesterId, cancellationToken);

        // Per-user counts (always returned). Scope: visible projects/tasks per role.
        var visibleProjectIds = await VisibleProjectIdsAsync(requester, cancellationToken);
        int projectCount = visibleProjectIds.Count;

        // Single round-trip for task counts: use COUNT() with conditional sums via group-by-constant.
        // EF translates this to: SELECT COUNT(*), SUM(CASE WHEN ...) FROM Tasks WHERE ProjectId IN (...).
        var taskAggregates = visibleProjectIds.Count == 0
            ? new TaskAggregates()
            : await _context.Tasks
                .AsNoTracking()
                .Where(t => visibleProjectIds.Contains(t.ProjectId) && !t.IsDeleted)
                .Where(t => requester.Role == AppRoles.Admin || IsTaskVisibleToMember(t, requesterId))
                .GroupBy(_ => 1)
                .Select(g => new TaskAggregates
                {
                    Total = g.Count(),
                    ForReview = g.Count(t => t.StatusId == StatusIdForReview),
                    Completed = g.Count(t => t.StatusId == StatusIdCompleted)
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? new TaskAggregates();

        var myCounts = new DashboardUserCounts
        {
            Projects = projectCount,
            Tasks = taskAggregates.Total,
            ForReview = taskAggregates.ForReview,
            Completed = taskAggregates.Completed
        };

        // Admin-only system counts. Run all four counts in a single round-trip via Task.WhenAll
        // (each is its own SQL query, but they're parallelisable on the same DbContext only when
        // using separate contexts — so we just await them serially; they're cheap aggregates).
        DashboardAdminCounts? adminCounts = null;
        if (requester.Role == AppRoles.Admin)
        {
            var now = PhTime;
            var totalUsers = await _context.Accounts.AsNoTracking().CountAsync(a => a.isActive, cancellationToken);
            var totalProjects = await _context.Projects.AsNoTracking().CountAsync(p => !p.IsDeleted, cancellationToken);
            var overdueTasks = await _context.Tasks.AsNoTracking()
                .CountAsync(t => !t.IsDeleted && t.DueDate < now && t.StatusId != StatusIdCompleted, cancellationToken);
            var deactivatedUsers = await _context.Accounts.AsNoTracking().CountAsync(a => !a.isActive, cancellationToken);

            adminCounts = new DashboardAdminCounts
            {
                TotalUsers = totalUsers,
                TotalProjects = totalProjects,
                OverdueTasks = overdueTasks,
                DeactivatedUsers = deactivatedUsers
            };
        }

        return new DashboardSummaryResponse
        {
            Role = requester.Role,
            MyCounts = myCounts,
            AdminCounts = adminCounts
        };
    }

    // ----------------------------------------------------------------------
    // Projects + task tree
    // ----------------------------------------------------------------------

    public async Task<IReadOnlyList<DashboardProjectResponse>> GetMyProjectsAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        var requester = await GetRequesterAsync(requesterId, cancellationToken);
        var visibleProjectIds = await VisibleProjectIdsAsync(requester, cancellationToken);

        if (visibleProjectIds.Count == 0)
            return Array.Empty<DashboardProjectResponse>();

        var projects = await _context.Projects
            .AsNoTracking()
            .Where(p => visibleProjectIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.StatusId,
                StatusName = p.Status.Name,
                p.CreatedAt,
                p.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var allTasks = await _context.Tasks
            .AsNoTracking()
            .Where(t => visibleProjectIds.Contains(t.ProjectId) && !t.IsDeleted)
            .Select(t => new TaskRow
            {
                Id = t.Id,
                ProjectId = t.ProjectId,
                Title = t.Title,
                Description = t.Description,
                StatusId = t.StatusId,
                StatusName = t.Status.Name,
                PriorityId = t.PriorityId,
                PriorityName = t.Priority.Name,
                StoryPoints = t.StoryPoints,
                StartDate = t.StartDate,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                ParentTaskId = t.ParentTaskId,
                AssigneeIds = t.Assignments.Where(a => !a.IsDeleted).Select(a => a.AccountId).ToList()
            })
            .ToListAsync(cancellationToken);

        // For non-admin members in projects where they're not PM/SM, hide root tasks they're not assigned to.
        var privilegedProjectIds = await PrivilegedProjectIdsAsync(requester, visibleProjectIds, cancellationToken);

        var byProject = allTasks.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<DashboardProjectResponse>(projects.Count);

        foreach (var project in projects)
        {
            var tasks = byProject.TryGetValue(project.Id, out var pTasks) ? pTasks : new List<TaskRow>();
            var childrenByParent = tasks
                .Where(t => t.ParentTaskId.HasValue)
                .GroupBy(t => t.ParentTaskId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).ToList());

            var rootTasks = tasks.Where(t => !t.ParentTaskId.HasValue).ToList();
            if (requester.Role != AppRoles.Admin && !privilegedProjectIds.Contains(project.Id))
            {
                rootTasks = rootTasks.Where(t => t.AssigneeIds.Contains(requesterId)).ToList();
            }

            DashboardTaskResponse Build(TaskRow row)
            {
                var children = childrenByParent.TryGetValue(row.Id, out var list)
                    ? list.Select(Build).ToList()
                    : new List<DashboardTaskResponse>();

                return new DashboardTaskResponse
                {
                    Id = row.Id,
                    Title = row.Title,
                    Description = row.Description,
                    StatusId = row.StatusId,
                    StatusName = row.StatusName,
                    PriorityId = row.PriorityId,
                    PriorityName = row.PriorityName,
                    StoryPoints = row.StoryPoints,
                    StartDate = row.StartDate,
                    DueDate = row.DueDate,
                    CreatedAt = row.CreatedAt,
                    UpdatedAt = row.UpdatedAt,
                    AssigneeIds = row.AssigneeIds,
                    Subtasks = children
                };
            }

            result.Add(new DashboardProjectResponse
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                StatusId = project.StatusId,
                StatusName = project.StatusName,
                CreatedAt = project.CreatedAt,
                UpdatedAt = project.UpdatedAt,
                Tasks = rootTasks.OrderBy(t => t.Id).Select(Build).ToList()
            });
        }

        return result;
    }

    // ----------------------------------------------------------------------
    // Calendar (flat task projection)
    // ----------------------------------------------------------------------

    public async Task<IReadOnlyList<CalendarTaskResponse>> GetCalendarTasksAsync(int requesterId,
        DateTime? from, DateTime? to, CancellationToken cancellationToken = default)
    {
        var requester = await GetRequesterAsync(requesterId, cancellationToken);
        var visibleProjectIds = await VisibleProjectIdsAsync(requester, cancellationToken);

        if (visibleProjectIds.Count == 0)
            return Array.Empty<CalendarTaskResponse>();

        var query = _context.Tasks
            .AsNoTracking()
            .Where(t => visibleProjectIds.Contains(t.ProjectId) && !t.IsDeleted && t.DueDate != null);

        if (requester.Role != AppRoles.Admin)
            query = query.Where(t => IsTaskVisibleToMember(t, requesterId));

        if (from.HasValue)
            query = query.Where(t => t.DueDate >= from.Value);
        if (to.HasValue)
            query = query.Where(t => t.DueDate <= to.Value);

        return await query
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Id)
            .Select(t => new CalendarTaskResponse
            {
                Id = t.Id,
                ProjectId = t.ProjectId,
                ProjectName = t.Project.Name,
                Title = t.Title,
                StatusId = t.StatusId,
                StatusName = t.Status.Name,
                PriorityId = t.PriorityId,
                PriorityName = t.Priority.Name,
                StartDate = t.StartDate,
                DueDate = t.DueDate,
                SubtaskCount = _context.Tasks.Count(c => c.ParentTaskId == t.Id && !c.IsDeleted),
                AssigneeIds = t.Assignments.Where(a => !a.IsDeleted).Select(a => a.AccountId).ToList()
            })
            .ToListAsync(cancellationToken);
    }

    // ----------------------------------------------------------------------
    // Project task summary (pie chart)
    // ----------------------------------------------------------------------

    public async Task<ProjectTaskSummaryResponse> GetProjectTaskSummaryAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var requester = await GetRequesterAsync(requesterId, cancellationToken);

        var project = await _context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && !p.IsDeleted)
            .Select(p => new { p.Id, p.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (project == null)
            throw new NotFoundException("Project not found.");

        var memberRole = requester.Role == AppRoles.Admin
            ? null
            : await _context.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == projectId && m.AccountId == requesterId && !m.IsDeleted)
                .Select(m => m.Role)
                .FirstOrDefaultAsync(cancellationToken);

        if (requester.Role != AppRoles.Admin && memberRole == null)
            throw new ForbiddenException("You are not a member of this project.");

        var isPrivileged = requester.Role == AppRoles.Admin
            || memberRole == AppRoles.ProjectManager
            || memberRole == AppRoles.ScrumMaster
            || memberRole == AppRoles.PmScrumMaster;

        IQueryable<TaskItem> taskQuery = _context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && !t.IsDeleted);

        if (!isPrivileged)
        {
            // Plain members see only their assigned tasks for the chart.
            taskQuery = taskQuery.Where(t => _context.TaskAssignments
                .Any(a => a.TaskId == t.Id && a.AccountId == requesterId && !a.IsDeleted));
        }

        // Single SQL: COUNT + 4 conditional aggregates.
        var counts = await taskQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                NotStarted = g.Count(t => t.StatusId == StatusIdNotStarted),
                InProgress = g.Count(t => t.StatusId == StatusIdInProgress),
                ForReview = g.Count(t => t.StatusId == StatusIdForReview),
                Completed = g.Count(t => t.StatusId == StatusIdCompleted)
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? new { Total = 0, NotStarted = 0, InProgress = 0, ForReview = 0, Completed = 0 };

        static double Pct(int part, int total) => total == 0 ? 0.0 : Math.Round((double)part / total * 100, 2);

        var breakdown = new List<ProjectTaskSummaryItem>
        {
            new() { StatusId = StatusIdNotStarted, StatusName = "Not Started", Count = counts.NotStarted, Percentage = Pct(counts.NotStarted, counts.Total) },
            new() { StatusId = StatusIdInProgress, StatusName = "In Progress", Count = counts.InProgress, Percentage = Pct(counts.InProgress, counts.Total) },
            new() { StatusId = StatusIdForReview, StatusName = "For Review", Count = counts.ForReview, Percentage = Pct(counts.ForReview, counts.Total) },
            new() { StatusId = StatusIdCompleted, StatusName = "Completed", Count = counts.Completed, Percentage = Pct(counts.Completed, counts.Total) },
        };

        return new ProjectTaskSummaryResponse
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            TotalTasks = counts.Total,
            CompletionPercentage = Pct(counts.Completed, counts.Total),
            Breakdown = breakdown
        };
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    private async Task<RequesterInfo> GetRequesterAsync(int requesterId, CancellationToken cancellationToken)
    {
        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new RequesterInfo { Id = a.Id, Role = a.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (requester == null)
            throw new ForbiddenException("Requester account not found.");
        return requester;
    }

    private async Task<IReadOnlyList<int>> VisibleProjectIdsAsync(RequesterInfo requester,
        CancellationToken cancellationToken)
    {
        if (requester.Role == AppRoles.Admin)
        {
            return await _context.Projects
                .AsNoTracking()
                .Where(p => !p.IsDeleted)
                .Select(p => p.Id)
                .ToListAsync(cancellationToken);
        }

        return await _context.Projects
            .AsNoTracking()
            .Where(p => !p.IsDeleted &&
                _context.ProjectMembers.Any(m => m.ProjectId == p.Id && m.AccountId == requester.Id && !m.IsDeleted))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<HashSet<int>> PrivilegedProjectIdsAsync(RequesterInfo requester,
        IReadOnlyList<int> visibleProjectIds, CancellationToken cancellationToken)
    {
        if (requester.Role == AppRoles.Admin)
            return new HashSet<int>(visibleProjectIds);

        var ids = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.AccountId == requester.Id && !m.IsDeleted &&
                visibleProjectIds.Contains(m.ProjectId) &&
                (m.Role == AppRoles.ProjectManager ||
                 m.Role == AppRoles.ScrumMaster ||
                 m.Role == AppRoles.PmScrumMaster))
            .Select(m => m.ProjectId)
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }

    /// <summary>
    /// Translatable predicate that returns true for tasks the (non-admin) requester is allowed to see —
    /// either they're directly assigned, or they're a privileged member (PM/SM/PM-SM) of the project.
    /// </summary>
    private bool IsTaskVisibleToMember(TaskItem t, int requesterId) =>
        _context.TaskAssignments.Any(a => a.TaskId == t.Id && a.AccountId == requesterId && !a.IsDeleted) ||
        _context.ProjectMembers.Any(m => m.ProjectId == t.ProjectId && m.AccountId == requesterId && !m.IsDeleted &&
            (m.Role == AppRoles.ProjectManager ||
             m.Role == AppRoles.ScrumMaster ||
             m.Role == AppRoles.PmScrumMaster));

    private sealed class RequesterInfo
    {
        public int Id { get; init; }
        public string Role { get; init; } = string.Empty;
    }

    private sealed class TaskAggregates
    {
        public int Total { get; init; }
        public int ForReview { get; init; }
        public int Completed { get; init; }
    }

    private sealed class TaskRow
    {
        public int Id { get; init; }
        public int ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public int StatusId { get; init; }
        public string StatusName { get; init; } = string.Empty;
        public int? PriorityId { get; init; }
        public string PriorityName { get; init; } = string.Empty;
        public int? StoryPoints { get; init; }
        public DateTime? StartDate { get; init; }
        public DateTime? DueDate { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? UpdatedAt { get; init; }
        public int? ParentTaskId { get; init; }
        public List<int> AssigneeIds { get; init; } = new();
    }
}
