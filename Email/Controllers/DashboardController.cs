using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Data;
using TaskManagement.Models;

namespace TaskManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase     
    {
        private readonly AccountDbContext _context;
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
        private static DateTime PhTime =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
        public DashboardController(AccountDbContext context)
        {
            _context = context;
        }

        [HttpGet("GetDashboardAdminStats")]
        public async Task<IActionResult> GetDashboardAdminStats()
        {
            try
            {
                var totalUsers = await _context.Accounts
                    .Where(a => a.isActive)
                    .CountAsync();

                var totalProjects = await _context.Projects
                    .Where(p => !p.IsDeleted)
                    .CountAsync();

                var overdueTasks = await _context.Tasks
                    .Where(t => !t.IsDeleted && t.DueDate < PhTime && t.StatusId != 4)
                    .CountAsync();

                var deactivatedUsers = await _context.Accounts
                    .Where(a => !a.isActive)
                    .CountAsync();
                return Ok(new
                {
                    totalUsers,
                    totalProjects,
                    overdueTasks,
                    deactivatedUsers
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("GetDashboardUserStats")]
        public async Task<IActionResult> GetDashboardUserStats()
        {
            try
            {
                var totalUsers = await _context.Accounts
                    .Where(a => a.isActive)
                    .CountAsync();

                var totalProjects = await _context.Projects
                    .Where(p => !p.IsDeleted)
                    .CountAsync();

                var overdueTasks = await _context.Tasks
                    .Where(t => !t.IsDeleted && t.DueDate < PhTime && t.StatusId != 4)
                    .CountAsync();

                var completedTasks = await _context.Tasks
                    .Where(t => !t.IsDeleted && t.StatusId == 4)
                    .CountAsync();

                return Ok(new
                {
                    totalUsers,
                    totalProjects,
                    overdueTasks,
                    completedTasks
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
        [HttpGet("MyProjectsAndTasks")]   // return all projects and tasks with subtasks
        public async Task<IActionResult> GetMyProjectsAndTasks([FromQuery] int requesterId)
        {
            try
            {
                var requester = await _context.Accounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == requesterId);
                if (requester == null)
                    return NotFound("Account not found.");

                IQueryable<Project> projectQuery = _context.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted);

                if (requester.Role != "Admin")
                {
                    projectQuery = projectQuery.Where(p =>
                        _context.ProjectMembers.Any(m =>
                            m.ProjectId == p.Id &&
                            m.AccountId == requesterId &&
                            !m.IsDeleted));
                }

                var projects = await projectQuery
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
                    .ToListAsync();

                if (projects.Count == 0)
                    return Ok(Array.Empty<object>());

                var projectIds = projects.Select(p => p.Id).ToList();

                var allTaskRows = await _context.Tasks
                    .AsNoTracking()
                    .Where(t => projectIds.Contains(t.ProjectId) && !t.IsDeleted)
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
                    .ToListAsync();

                var privilegedProjectIds = new HashSet<int>();
                if (requester.Role != "Admin")
                {
                    var privilegedRoles = new[] { "Project Manager", "Scrum Master", "Project Manager - Scrum Master" };
                    var ids = await _context.ProjectMembers
                        .AsNoTracking()
                        .Where(m =>
                            m.AccountId == requesterId &&
                            !m.IsDeleted &&
                            projectIds.Contains(m.ProjectId) &&
                            privilegedRoles.Contains(m.Role))
                        .Select(m => m.ProjectId)
                        .ToListAsync();
                    privilegedProjectIds = ids.ToHashSet();
                }

                var byProject = allTaskRows.GroupBy(t => t.ProjectId).ToDictionary(g => g.Key, g => g.ToList());
                var result = new List<object>(projects.Count);

                foreach (var project in projects)
                {
                    var tasks = byProject.TryGetValue(project.Id, out var pTasks) ? pTasks : new List<TaskRow>();
                    var childrenByParent = tasks
                        .Where(t => t.ParentTaskId.HasValue)
                        .GroupBy(t => t.ParentTaskId!.Value)
                        .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).ToList());

                    var rootTasks = tasks.Where(t => !t.ParentTaskId.HasValue).ToList();
                    if (requester.Role != "Admin" && !privilegedProjectIds.Contains(project.Id))
                    {
                        rootTasks = rootTasks
                            .Where(t => t.AssigneeIds.Contains(requesterId))
                            .ToList();
                    }

                    object BuildTaskTree(TaskRow row)
                    {
                        var children = childrenByParent.TryGetValue(row.Id, out var list) ? list : new List<TaskRow>();
                        return new
                        {
                            row.Id,
                            row.Title,
                            row.Description,
                            row.StatusId,
                            row.StatusName,
                            row.PriorityId,
                            row.PriorityName,
                            row.StoryPoints,
                            row.StartDate,
                            row.DueDate,
                            row.CreatedAt,
                            row.UpdatedAt,
                            row.AssigneeIds,
                            Subtasks = children.Select(BuildTaskTree).ToList()
                        };
                    }

                    result.Add(new
                    {
                        project.Id,
                        project.Name,
                        project.Description,
                        project.StatusId,
                        project.StatusName,
                        project.CreatedAt,
                        project.UpdatedAt,
                        Tasks = rootTasks.OrderBy(t => t.Id).Select(BuildTaskTree).ToList()
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("ProjectStatusSummary")] // return count and percentage (Active, Completed, Not Started)-PROJECTS
        public async Task<IActionResult> GetProjectStatusSummary([FromQuery] int requesterId)
        {
            try
            {
                var requester = await _context.Accounts.FindAsync(requesterId);
                if (requester == null)
                    return NotFound("Account not found.");

                IQueryable<Project> projectQuery = _context.Projects
                    .Where(p => !p.IsDeleted);

                if (requester.Role != "Admin")
                {
                    projectQuery = projectQuery.Where(p =>
                        _context.ProjectMembers.Any(m =>
                            m.ProjectId == p.Id &&
                            m.AccountId == requesterId &&
                            !m.IsDeleted));
                }

                var total = await projectQuery.CountAsync();

                var grouped = await projectQuery
                    .GroupBy(p => new { p.StatusId, p.Status.Name })
                    .Select(g => new
                    {
                        StatusId = g.Key.StatusId,
                        StatusName = g.Key.Name,
                        Count = g.Count()
                    })
                    .ToListAsync();

                var statuses = await _context.ProjectStatuses
                    .Where(s => s.IsActive)
                    .ToListAsync();

                var result = statuses.Select(s =>
                {
                    var match = grouped.FirstOrDefault(g => g.StatusId == s.Id);
                    var count = match?.Count ?? 0;
                    return new
                    {
                        StatusId = s.Id,
                        StatusName = s.Name,
                        Count = count,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)count / total * 100, 2)
                    };
                });

                return Ok(new
                {
                    TotalProjects = total,
                    Breakdown = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("TaskStatusSummary")] // return count and percentage (Not Started, In Progress, Completed, For Review)-PROJECTS
        public async Task<IActionResult> GetTaskStatusSummary([FromQuery] int requesterId)
        {
            try
            {
                var requester = await _context.Accounts.FindAsync(requesterId);
                if (requester == null)
                    return NotFound("Account not found.");

                IQueryable<TaskItem> taskQuery = _context.Tasks
                    .Where(t => !t.IsDeleted);

                if (requester.Role != "Admin")
                {
                    taskQuery = taskQuery.Where(t =>
                        _context.TaskAssignments.Any(a =>
                            a.TaskId == t.Id &&
                            a.AccountId == requesterId &&
                            !a.IsDeleted) ||
                        _context.ProjectMembers.Any(m =>
                            m.ProjectId == t.ProjectId &&
                            m.AccountId == requesterId &&
                            !m.IsDeleted &&
                            (m.Role == "Project Manager" ||
                             m.Role == "Scrum Master" ||
                             m.Role == "Project Manager - Scrum Master")));
                }

                var total = await taskQuery.CountAsync();

                var grouped = await taskQuery
                    .GroupBy(t => new { t.StatusId, t.Status.Name })
                    .Select(g => new
                    {
                        StatusId = g.Key.StatusId,
                        StatusName = g.Key.Name,
                        Count = g.Count()
                    })
                    .ToListAsync();

                var statuses = await _context.TaskStatuses
                    .Where(s => s.IsActive)
                    .ToListAsync();

                var result = statuses.Select(s =>
                {
                    var match = grouped.FirstOrDefault(g => g.StatusId == s.Id);
                    var count = match?.Count ?? 0;
                    return new
                    {
                        StatusId = s.Id,
                        StatusName = s.Name,
                        Count = count,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)count / total * 100, 2)
                    };
                });

                return Ok(new
                {
                    TotalTasks = total,
                    Breakdown = result
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("ProjectTaskSummary/{projectId}")] // return count and percentage (Completed, Working On)
        public async Task<IActionResult> GetProjectTaskSummary(int projectId, [FromQuery] int requesterId)
        {
            try
            {
                var requester = await _context.Accounts.FindAsync(requesterId);
                if (requester == null)
                    return NotFound("Account not found.");

                var project = await _context.Projects
                    .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted);
                if (project == null)
                    return NotFound("Project not found.");

                if (requester.Role != "Admin")
                {
                    var isMember = await _context.ProjectMembers
                        .AnyAsync(m => m.ProjectId == projectId && m.AccountId == requesterId && !m.IsDeleted);
                    if (!isMember)
                        return StatusCode(403, "You are not a member of this project.");
                }
                IQueryable<TaskItem> taskQuery = _context.Tasks
                 .Where(t => t.ProjectId == projectId && !t.IsDeleted);

                if (requester.Role != "Admin")
                {
                    var projectMember = await _context.ProjectMembers
                        .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == requesterId && !m.IsDeleted);

                    var isPrivileged = projectMember?.Role == "Project Manager" ||
                                       projectMember?.Role == "Scrum Master" ||
                                       projectMember?.Role == "Project Manager - Scrum Master";

                    if (!isPrivileged)
                    {
                        // members
                        taskQuery = taskQuery.Where(t =>
                            _context.TaskAssignments.Any(a =>
                                a.TaskId == t.Id &&
                                a.AccountId == requesterId &&
                                !a.IsDeleted));
                    }
                }
                var tasks = await taskQuery
                   .Select(t => new { t.StatusId })
                   .ToListAsync();

                var total = tasks.Count;
                var completed = tasks.Count(t => t.StatusId == 4);
                var forReview = tasks.Count(t => t.StatusId == 3);
                var inProgress = tasks.Count(t => t.StatusId == 2);
                var notStarted = tasks.Count(t => t.StatusId == 1);

                return Ok(new
                {
                    ProjectId = projectId,
                    ProjectName = project.Name,
                    TotalTasks = total,
                    CompletionPercentage = total == 0 ? 0.0 : Math.Round((double)completed / total * 100, 2), 
                    Completed = new
                    {
                        Count = completed,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)completed / total * 100, 2)
                    },
                    ForReview = new
                    {
                        Count = forReview,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)forReview / total * 100, 2)
                    },
                    InProgress = new
                    {
                        Count = inProgress,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)inProgress / total * 100, 2)
                    },
                    NotStarted = new
                    {
                        Count = notStarted,
                        Percentage = total == 0 ? 0.0 : Math.Round((double)notStarted / total * 100, 2)
                    }
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}