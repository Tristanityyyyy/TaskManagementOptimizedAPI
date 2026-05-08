using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Project;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class ProjectService : IProjectService
{
    private const int NotStartedStatusId = 1;
    private const int CompletedTaskStatusId = 4;

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IProjectAuthService _projectAuth;

    public ProjectService(AccountDbContext context, IProjectAuthService projectAuth, IEmailService emailService)
    {
        _context = context;
        _projectAuth = projectAuth;
        _emailService = emailService;
    }

    public async Task<PagedResult<ProjectListItemResponse>> ListAsync(int requesterId, int page, int pageSize,
        bool includeDeleted, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (requester == null)
            throw new ForbiddenException("Account not found.");

        var isAdmin = requester.Role == AppRoles.Admin;

        if (includeDeleted && !isAdmin)
            throw new ForbiddenException("Only administrators can browse deleted projects.");

        IQueryable<Project> query = _context.Projects.AsNoTracking();

        query = includeDeleted
            ? query.Where(p => p.IsDeleted)
            : query.Where(p => !p.IsDeleted);

        if (!isAdmin)
        {
            query = query.Where(p => p.Members.Any(m => m.AccountId == requesterId && !m.IsDeleted));
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectListItemResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                StatusId = p.StatusId,
                StatusName = p.Status.Name,
                CreatedById = p.CreatedById,
                CreatedByName = p.CreatedBy.Name,
                ProjectManagerId = p.ProjectManagerId,
                ProjectManagerName = p.Members
                    .Where(m => m.AccountId == p.ProjectManagerId && m.IsDeleted == p.IsDeleted)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                ScrumMasterId = p.ScrumMasterId,
                ScrumMasterName = p.Members
                    .Where(m => p.ScrumMasterId != null && m.AccountId == p.ScrumMasterId && m.IsDeleted == p.IsDeleted)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                AssigneeIds = p.Members
                    .Where(m => m.Role == "Member" && m.IsDeleted == p.IsDeleted)
                    .Select(m => m.AccountId)
                    .ToList(),
                MemberNames = p.Members
                    .Where(m => m.Role == "Member" && m.IsDeleted == p.IsDeleted)
                    .Select(m => m.Account.Name)
                    .ToList(),
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                DeletedAt = p.DeletedAt
            })
            .ToListAsync(cancellationToken);

        if (items.Count > 0 && !includeDeleted)
        {
            var ids = items.Select(p => p.Id).ToList();
            var completion = await GetCompletionMapAsync(ids, cancellationToken);
            foreach (var item in items)
                item.CompletionPercentage = completion.TryGetValue(item.Id, out var pct) ? pct : 0;
        }

        return new PagedResult<ProjectListItemResponse>(items, total, page, pageSize);
    }

    public async Task<ProjectListItemResponse?> FindAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Role })
            .FirstOrDefaultAsync(cancellationToken);

        if (requester == null)
            throw new ForbiddenException("Account not found.");

        var isAdmin = requester.Role == AppRoles.Admin;

        var project = await _context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId && !p.IsDeleted)
            .Select(p => new ProjectListItemResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                StatusId = p.StatusId,
                StatusName = p.Status.Name,
                CreatedById = p.CreatedById,
                CreatedByName = p.CreatedBy.Name,
                ProjectManagerId = p.ProjectManagerId,
                ProjectManagerName = p.Members
                    .Where(m => m.AccountId == p.ProjectManagerId && !m.IsDeleted)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                ScrumMasterId = p.ScrumMasterId,
                ScrumMasterName = p.Members
                    .Where(m => p.ScrumMasterId != null && m.AccountId == p.ScrumMasterId && !m.IsDeleted)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                AssigneeIds = p.Members
                    .Where(m => m.Role == "Member" && !m.IsDeleted)
                    .Select(m => m.AccountId)
                    .ToList(),
                MemberNames = p.Members
                    .Where(m => m.Role == "Member" && !m.IsDeleted)
                    .Select(m => m.Account.Name)
                    .ToList(),
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                DeletedAt = p.DeletedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
            return null;

        if (!isAdmin)
        {
            var isMember = await _context.ProjectMembers
                .AsNoTracking()
                .AnyAsync(m => m.ProjectId == projectId && m.AccountId == requesterId && !m.IsDeleted,
                    cancellationToken);

            if (!isMember)
                throw new ForbiddenException("You are not a member of this project.");
        }

        var completion = await GetCompletionMapAsync(new[] { project.Id }, cancellationToken);
        project.CompletionPercentage = completion.TryGetValue(project.Id, out var pct) ? pct : 0;

        return project;
    }

    public async Task<ProjectListItemResponse> CreateAsync(int creatorId, CreateProjectRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(nameof(request.Name), "Project name is required.");

        if (request.StartDate == default)
            throw new ValidationException(nameof(request.StartDate), "Start date is required.");

        if (request.EndDate == default)
            throw new ValidationException(nameof(request.EndDate), "End date is required.");

        if (request.EndDate <= request.StartDate)
            throw new ValidationException(nameof(request.EndDate), "End date must be after start date.");

        var creator = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == creatorId, cancellationToken)
            ?? throw new NotFoundException("Creator account not found.");

        var isAdmin = creator.Role == AppRoles.Admin;

        int projectManagerId;
        int? scrumMasterId;

        if (isAdmin)
        {
            if (!request.ProjectManagerId.HasValue)
                throw new ValidationException(nameof(request.ProjectManagerId),
                    "Admin must select a Project Manager.");

            projectManagerId = request.ProjectManagerId.Value;

            if (request.IsAlsoScrumMaster)
                scrumMasterId = projectManagerId;
            else
                scrumMasterId = request.ScrumMasterId;
        }
        else
        {
            projectManagerId = creatorId;
            scrumMasterId = request.IsAlsoScrumMaster
                ? creatorId
                : request.ScrumMasterId ?? creatorId;
        }

        var memberIds = request.MemberIds.Where(id => id > 0).Distinct().ToList();
        if (memberIds.Count != request.MemberIds.Count)
            throw new ValidationException(nameof(request.MemberIds), "Duplicate or invalid member IDs are not allowed.");

        var idsToValidate = new HashSet<int> { projectManagerId };
        if (scrumMasterId.HasValue) idsToValidate.Add(scrumMasterId.Value);
        foreach (var id in memberIds) idsToValidate.Add(id);

        var validIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => idsToValidate.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var missing = idsToValidate.Except(validIds).ToList();
        if (missing.Count > 0)
            throw new ValidationException(nameof(request.MemberIds),
                $"The following account IDs do not exist: {string.Join(", ", missing)}");

        var notStartedExists = await _context.ProjectStatuses
            .AsNoTracking()
            .AnyAsync(s => s.Id == NotStartedStatusId, cancellationToken);

        if (!notStartedExists)
            throw new ValidationException("status", "Default project status is not configured.");

        var project = new Project
        {
            Name = request.Name.Trim(),
            Description = request.Description,
            CreatedById = creatorId,
            ProjectManagerId = projectManagerId,
            ScrumMasterId = scrumMasterId,
            StatusId = NotStartedStatusId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            CreatedAt = PhTime,
            UpdatedAt = PhTime
        };

        _context.Projects.Add(project);

        var pmRoleLabel = scrumMasterId.HasValue && scrumMasterId == projectManagerId
            ? AppRoles.PmScrumMaster
            : AppRoles.ProjectManager;

        _context.ProjectMembers.Add(new ProjectMember
        {
            Project = project,
            AccountId = projectManagerId,
            Role = pmRoleLabel,
            JoinedAt = PhTime
        });

        if (scrumMasterId.HasValue && scrumMasterId.Value != projectManagerId)
        {
            _context.ProjectMembers.Add(new ProjectMember
            {
                Project = project,
                AccountId = scrumMasterId.Value,
                Role = AppRoles.ScrumMaster,
                JoinedAt = PhTime
            });
        }

        foreach (var memberId in memberIds)
        {
            if (memberId == projectManagerId || memberId == scrumMasterId)
                continue;

            _context.ProjectMembers.Add(new ProjectMember
            {
                Project = project,
                AccountId = memberId,
                Role = "Member",
                JoinedAt = PhTime
            });
        }

        _context.AuditLogs.Add(new AuditLog
        {
            Project = project,
            TaskId = null,
            AccountId = creatorId,
            Action = "CREATED",
            NewValue = project.Name,
            Note = $"Created project '{project.Name}' by {creator.Name}.",
            CreatedAt = PhTime
        });

        await _context.SaveChangesAsync(cancellationToken);

        var notifications = new List<Notification>();

        if (!scrumMasterId.HasValue || scrumMasterId.Value == projectManagerId)
        {
            notifications.Add(NewNotification(projectManagerId, project.Id,
                $"You have been assigned as Project Manager - Scrum Master of '{project.Name}'."));
        }
        else
        {
            notifications.Add(NewNotification(projectManagerId, project.Id,
                $"You have been assigned as Project Manager of '{project.Name}'."));
            notifications.Add(NewNotification(scrumMasterId.Value, project.Id,
                $"You have been assigned as Scrum Master of '{project.Name}'."));
        }

        foreach (var memberId in memberIds)
        {
            if (memberId == projectManagerId || memberId == scrumMasterId)
                continue;

            notifications.Add(NewNotification(memberId, project.Id,
                $"You have been added as a Member to project '{project.Name}'."));
        }

        if (notifications.Count > 0)
        {
            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync(cancellationToken);
        }

        await SendCreationEmailsAsync(project, projectManagerId, scrumMasterId, memberIds, cancellationToken);

        var listing = await FindAsync(creatorId, project.Id, cancellationToken)
                      ?? throw new NotFoundException("Project not found after creation.");

        return listing;
    }

    public async Task<ProjectListItemResponse> UpdateAsync(int requesterId, int projectId,
        UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        var requester = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == requesterId, cancellationToken)
            ?? throw new NotFoundException("Requester account not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);

        if (!membership.IsAdmin && !membership.IsProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can update this project.");

        var changes = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name != project.Name)
        {
            changes.Add($"Name: {project.Name} → {request.Name}");
            project.Name = request.Name.Trim();
        }

        if (request.Description != null && request.Description != project.Description)
        {
            changes.Add("Description updated");
            project.Description = request.Description;
        }

        if (request.StatusId.HasValue && request.StatusId.Value != project.StatusId)
        {
            var statusExists = await _context.ProjectStatuses
                .AnyAsync(s => s.Id == request.StatusId.Value, cancellationToken);

            if (!statusExists)
                throw new ValidationException(nameof(request.StatusId), "Invalid StatusId.");

            changes.Add($"StatusId: {project.StatusId} → {request.StatusId}");
            project.StatusId = request.StatusId.Value;
        }

        var startDate = request.StartDate ?? project.StartDate;
        var endDate = request.EndDate ?? project.EndDate;
        if (endDate <= startDate)
            throw new ValidationException(nameof(request.EndDate), "End date must be after start date.");

        if (request.StartDate.HasValue && request.StartDate.Value != project.StartDate)
        {
            changes.Add($"StartDate: {project.StartDate:yyyy-MM-dd} → {request.StartDate:yyyy-MM-dd}");
            project.StartDate = request.StartDate.Value;
        }

        if (request.EndDate.HasValue && request.EndDate.Value != project.EndDate)
        {
            changes.Add($"EndDate: {project.EndDate:yyyy-MM-dd} → {request.EndDate:yyyy-MM-dd}");
            project.EndDate = request.EndDate.Value;
        }

        // Only Admin can change Project Manager.
        if (membership.IsAdmin && request.ProjectManagerId.HasValue &&
            request.ProjectManagerId.Value != project.ProjectManagerId)
        {
            var newPmId = request.ProjectManagerId.Value;

            var pmExists = await _context.Accounts.AnyAsync(a => a.Id == newPmId, cancellationToken);
            if (!pmExists)
                throw new ValidationException(nameof(request.ProjectManagerId),
                    $"Account with ID {newPmId} does not exist.");

            var oldPmMembership = await _context.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == projectId &&
                                          m.AccountId == project.ProjectManagerId &&
                                          !m.IsDeleted, cancellationToken);

            if (oldPmMembership != null)
            {
                if (oldPmMembership.Role == AppRoles.PmScrumMaster)
                    oldPmMembership.Role = AppRoles.ScrumMaster;
                else
                {
                    oldPmMembership.IsDeleted = true;
                    oldPmMembership.DeletedAt = PhTime;
                }
            }

            await UpsertManagerMembershipAsync(projectId, newPmId, isPm: true, cancellationToken);

            changes.Add($"Project Manager: {project.ProjectManagerId} → {newPmId}");
            project.ProjectManagerId = newPmId;
        }

        if (request.ScrumMasterId != project.ScrumMasterId)
        {
            if (request.ScrumMasterId.HasValue)
            {
                var smExists = await _context.Accounts.AnyAsync(a => a.Id == request.ScrumMasterId.Value,
                    cancellationToken);
                if (!smExists)
                    throw new ValidationException(nameof(request.ScrumMasterId),
                        $"Account with ID {request.ScrumMasterId} does not exist.");
            }

            if (project.ScrumMasterId.HasValue)
            {
                var oldSm = await _context.ProjectMembers
                    .FirstOrDefaultAsync(m => m.ProjectId == projectId &&
                                              m.AccountId == project.ScrumMasterId.Value &&
                                              !m.IsDeleted, cancellationToken);

                if (oldSm != null)
                {
                    if (oldSm.Role == AppRoles.PmScrumMaster)
                        oldSm.Role = AppRoles.ProjectManager;
                    else if (oldSm.Role == AppRoles.ScrumMaster)
                    {
                        oldSm.IsDeleted = true;
                        oldSm.DeletedAt = PhTime;
                    }
                }
            }

            if (request.ScrumMasterId.HasValue)
            {
                await UpsertManagerMembershipAsync(projectId, request.ScrumMasterId.Value,
                    isPm: false, cancellationToken);
            }

            changes.Add($"Scrum Master: {project.ScrumMasterId} → {request.ScrumMasterId}");
            project.ScrumMasterId = request.ScrumMasterId;
        }

        if (request.AssigneeIds != null)
        {
            var assignees = request.AssigneeIds.Where(id => id > 0).Distinct().ToList();
            if (assignees.Count != request.AssigneeIds.Count)
                throw new ValidationException(nameof(request.AssigneeIds),
                    "Duplicate or invalid assignee IDs are not allowed.");

            if (assignees.Count > 0)
            {
                var validIds = await _context.Accounts
                    .AsNoTracking()
                    .Where(a => assignees.Contains(a.Id))
                    .Select(a => a.Id)
                    .ToListAsync(cancellationToken);

                var missing = assignees.Except(validIds).ToList();
                if (missing.Count > 0)
                    throw new ValidationException(nameof(request.AssigneeIds),
                        $"The following account IDs do not exist: {string.Join(", ", missing)}");
            }

            var existingMembers = await _context.ProjectMembers
                .Where(m => m.ProjectId == projectId && m.Role == "Member")
                .ToListAsync(cancellationToken);

            foreach (var existing in existingMembers)
            {
                var shouldBePresent = assignees.Contains(existing.AccountId)
                                      && existing.AccountId != project.ProjectManagerId
                                      && existing.AccountId != project.ScrumMasterId;

                if (existing.IsDeleted && shouldBePresent)
                {
                    existing.IsDeleted = false;
                    existing.DeletedAt = null;
                    existing.Role = "Member";
                }
                else if (!existing.IsDeleted && !shouldBePresent)
                {
                    existing.IsDeleted = true;
                    existing.DeletedAt = PhTime;
                }
            }

            var existingIds = existingMembers.Select(m => m.AccountId).ToHashSet();
            foreach (var memberId in assignees)
            {
                if (memberId == project.ProjectManagerId || memberId == project.ScrumMasterId)
                    continue;

                if (existingIds.Contains(memberId))
                    continue;

                _context.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    AccountId = memberId,
                    Role = "Member",
                    JoinedAt = PhTime
                });
            }

            changes.Add("Assignees updated");
        }

        if (changes.Count == 0)
            return await FindAsync(requesterId, projectId, cancellationToken)
                ?? throw new NotFoundException("Project not found.");

        project.UpdatedAt = PhTime;

        var requesterRole = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";
        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = project.Id,
            TaskId = null,
            AccountId = requesterId,
            Action = "UPDATED",
            NewValue = string.Join(", ", changes),
            Note = $"Project updated '{project.Name}' by {requester.Name} ({requesterRole}).",
            CreatedAt = PhTime
        });

        await _context.SaveChangesAsync(cancellationToken);

        await SendUpdateEmailsAsync(project, requester, changes, cancellationToken);

        return await FindAsync(requesterId, projectId, cancellationToken)
               ?? throw new NotFoundException("Project not found.");
    }

    public async Task SoftDeleteAsync(int requesterId, int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        if (project.IsDeleted)
            throw new NotFoundException("Project not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);
        if (!membership.IsAdmin && !membership.IsProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can delete this project.");

        var requesterAccount = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == requesterId, cancellationToken)
            ?? throw new NotFoundException("Requester account not found.");

        var now = PhTime;

        await _context.ProjectMembers
            .Where(m => m.ProjectId == projectId && !m.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDeleted, true)
                .SetProperty(m => m.DeletedAt, now), cancellationToken);

        await _context.TaskAssignments
            .Where(a => a.Task.ProjectId == projectId && !a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, true)
                .SetProperty(a => a.DeletedAt, now), cancellationToken);

        await _context.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, true)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        project.IsDeleted = true;
        project.DeletedAt = now;
        project.UpdatedAt = now;

        var deleterRole = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = project.Id,
            TaskId = null,
            AccountId = requesterId,
            Action = "DELETED",
            OldValue = project.Name,
            Note = $"Project '{project.Name}' deleted by {requesterAccount.Name} ({deleterRole}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(int requesterId, int projectId, CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken)
            ?? throw new NotFoundException("Project not found.");

        if (!project.IsDeleted)
            throw new NotFoundException("Project not found.");

        var requesterAccount = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == requesterId, cancellationToken)
            ?? throw new NotFoundException("Requester account not found.");

        // Check role at the moment of deletion: requester must be Admin or have been a PM.
        var isAdmin = requesterAccount.Role == AppRoles.Admin;
        var pmRole = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.AccountId == requesterId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        var wasProjectManager = pmRole == AppRoles.ProjectManager || pmRole == AppRoles.PmScrumMaster;
        if (!isAdmin && !wasProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can restore this project.");

        var now = PhTime;

        await _context.TaskAssignments
            .Where(a => a.Task.ProjectId == projectId && a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, false)
                .SetProperty(a => a.DeletedAt, (DateTime?)null), cancellationToken);

        await _context.Tasks
            .Where(t => t.ProjectId == projectId && t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, false)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        await _context.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDeleted, false)
                .SetProperty(m => m.DeletedAt, (DateTime?)null), cancellationToken);

        project.IsDeleted = false;
        project.DeletedAt = null;
        project.UpdatedAt = now;

        var roleLabel = isAdmin ? AppRoles.Admin : pmRole ?? "Unknown";
        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = project.Id,
            TaskId = null,
            AccountId = requesterId,
            Action = "RESTORED",
            NewValue = project.Name,
            Note = $"Project and all tasks reactivated by {requesterAccount.Name} ({roleLabel}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectStatusItem>> GetStatusesCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ProjectStatuses
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .Select(s => new ProjectStatusItem(s.Id, s.Name, s.Description, s.IsActive, s.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task UpsertManagerMembershipAsync(int projectId, int accountId, bool isPm,
        CancellationToken cancellationToken)
    {
        var existing = await _context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == accountId, cancellationToken);

        if (existing == null)
        {
            _context.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                AccountId = accountId,
                Role = isPm ? AppRoles.ProjectManager : AppRoles.ScrumMaster,
                JoinedAt = PhTime
            });
            return;
        }

        existing.IsDeleted = false;
        existing.DeletedAt = null;

        existing.Role = isPm
            ? (existing.Role == AppRoles.ScrumMaster ? AppRoles.PmScrumMaster : AppRoles.ProjectManager)
            : (existing.Role == AppRoles.ProjectManager ? AppRoles.PmScrumMaster : AppRoles.ScrumMaster);
    }

    private async Task<Dictionary<int, int>> GetCompletionMapAsync(IEnumerable<int> projectIds,
        CancellationToken cancellationToken)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, int>();

        var rows = await _context.Tasks
            .AsNoTracking()
            .Where(t => ids.Contains(t.ProjectId) && !t.IsDeleted && t.ParentTaskId == null)
            .GroupBy(t => t.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Total = g.Count(),
                Completed = g.Count(x => x.StatusId == CompletedTaskStatusId)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.ProjectId,
            x => x.Total == 0 ? 0 : (int)Math.Round((double)x.Completed / x.Total * 100));
    }

    private static Notification NewNotification(int accountId, int projectId, string message) => new()
    {
        AccountId = accountId,
        Message = message,
        ProjectId = projectId,
        IsRead = false,
        CreatedAt = PhTime
    };

    private async Task SendCreationEmailsAsync(Project project, int projectManagerId, int? scrumMasterId,
        IReadOnlyCollection<int> memberIds, CancellationToken cancellationToken)
    {
        var notifyIds = new HashSet<int> { projectManagerId };
        if (scrumMasterId.HasValue) notifyIds.Add(scrumMasterId.Value);
        foreach (var id in memberIds) notifyIds.Add(id);

        var accounts = await _context.Accounts
            .AsNoTracking()
            .Where(a => notifyIds.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.Email })
            .ToListAsync(cancellationToken);

        var byId = accounts.ToDictionary(a => a.Id, a => a);

        if (byId.TryGetValue(projectManagerId, out var pm) && !string.IsNullOrEmpty(pm.Email))
        {
            var label = scrumMasterId.HasValue && scrumMasterId.Value == projectManagerId
                ? "Project Manager &amp; Scrum Master"
                : "Project Manager";

            await _emailService.SendEmailAsync(pm.Email,
                $"You've been assigned to project: {project.Name}",
                BuildAssignmentEmail(pm.Name, label, project));
        }

        if (scrumMasterId.HasValue && scrumMasterId.Value != projectManagerId &&
            byId.TryGetValue(scrumMasterId.Value, out var sm) && !string.IsNullOrEmpty(sm.Email))
        {
            await _emailService.SendEmailAsync(sm.Email,
                $"You've been assigned to project: {project.Name}",
                BuildAssignmentEmail(sm.Name, "Scrum Master", project));
        }

        foreach (var memberId in memberIds)
        {
            if (memberId == projectManagerId || memberId == scrumMasterId)
                continue;

            if (!byId.TryGetValue(memberId, out var member) || string.IsNullOrEmpty(member.Email))
                continue;

            await _emailService.SendEmailAsync(member.Email,
                $"You've been added to project: {project.Name}",
                BuildAssignmentEmail(member.Name, "Member", project));
        }
    }

    private async Task SendUpdateEmailsAsync(Project project, Account requester, IReadOnlyCollection<string> changes,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<int> { project.ProjectManagerId };
        if (project.ScrumMasterId.HasValue) ids.Add(project.ScrumMasterId.Value);

        var accounts = await _context.Accounts
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.Email })
            .ToListAsync(cancellationToken);

        var changesHtml = string.Join("", changes.Select(c => $"<li>{System.Net.WebUtility.HtmlEncode(c)}</li>"));

        foreach (var account in accounts)
        {
            if (string.IsNullOrEmpty(account.Email))
                continue;

            var body = $@"<h2>Project Update Notification</h2>
                          <p>Hello <strong>{account.Name}</strong>,</p>
                          <p>The project <strong>{project.Name}</strong> has been updated by <strong>{requester.Name}</strong>.</p>
                          <p><strong>Changes:</strong></p>
                          <ul>{changesHtml}</ul>
                          <p>Please log in to review the changes.</p>";

            await _emailService.SendEmailAsync(account.Email,
                $"Project Updated: {project.Name}", body);
        }
    }

    private static string BuildAssignmentEmail(string name, string roleLabel, Project project) => $@"
        <h2>Project Assignment</h2>
        <p>Hello <strong>{name}</strong>,</p>
        <p>You have been assigned as <strong>{roleLabel}</strong> of project <strong>{project.Name}</strong>.</p>
        <p><strong>Start Date:</strong> {project.StartDate:MMMM dd, yyyy}</p>
        <p><strong>End Date:</strong> {project.EndDate:MMMM dd, yyyy}</p>
        <p>Please log in to view the project details.</p>";
}
