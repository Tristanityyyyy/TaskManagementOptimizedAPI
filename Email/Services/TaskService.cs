using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Task;
using TaskManagement.Exceptions;
using TaskManagement.Helpers;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class TaskService : ITaskService
{
    private static readonly int[] ValidFibonacciStoryPoints = [1, 2, 3, 5, 8, 13, 21];

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IProjectAuthService _projectAuth;

    public TaskService(AccountDbContext context, IProjectAuthService projectAuth, IEmailService emailService)
    {
        _context = context;
        _projectAuth = projectAuth;
        _emailService = emailService;
    }

    public async Task<PagedResult<TaskListItemResponse>> ListByProjectAsync(int requesterId, int projectId,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var visibility = await _projectAuth.GetTaskVisibilityAsync(requesterId, projectId, cancellationToken);
        if (visibility == TaskVisibility.None)
            throw new ForbiddenException("You are not a member of this project.");

        IQueryable<TaskItem> query = _context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && !t.IsDeleted);

        if (visibility == TaskVisibility.OnlyAssigned)
            query = query.Where(t =>
                t.Assignments.Any(a => a.AccountId == requesterId && !a.IsDeleted));

        return await PageAndProjectAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<PagedResult<TaskListItemResponse>> ListAssignedToMeAsync(int requesterId, int page,
        int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Tasks
            .AsNoTracking()
            .Where(t => !t.IsDeleted &&
                        t.Assignments.Any(a => a.AccountId == requesterId && !a.IsDeleted));

        return await PageAndProjectAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<TaskListItemResponse?> FindAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new
            {
                t.Id,
                t.ProjectId,
                Item = new TaskListItemResponse
                {
                    Id = t.Id,
                    Title = t.Title,
                    Description = t.Description,
                    StatusId = t.StatusId,
                    StatusName = t.Status.Name,
                    PriorityId = t.PriorityId,
                    PriorityName = t.Priority != null ? t.Priority.Name : null,
                    CreatorId = t.CreatorId,
                    CreatorName = t.Creator.Name,
                    StoryPoints = t.StoryPoints,
                    ProjectId = t.ProjectId,
                    ParentTaskId = t.ParentTaskId,
                    StartDate = t.StartDate,
                    DueDate = t.DueDate,
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt,
                    AssigneeIds = t.Assignments.Where(a => !a.IsDeleted).Select(a => a.AccountId).ToList()
                }
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (task == null)
            return null;

        var visibility = await _projectAuth.GetTaskVisibilityAsync(requesterId, task.ProjectId, cancellationToken);
        if (visibility == TaskVisibility.None)
            throw new ForbiddenException("You are not a member of this project.");

        if (visibility == TaskVisibility.OnlyAssigned &&
            !task.Item.AssigneeIds.Contains(requesterId))
            throw new ForbiddenException("You are not assigned to this task.");

        return task.Item;
    }

    private async Task<PagedResult<TaskListItemResponse>> PageAndProjectAsync(
        IQueryable<TaskItem> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new TaskListItemResponse
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                StatusId = t.StatusId,
                StatusName = t.Status.Name,
                PriorityId = t.PriorityId,
                PriorityName = t.Priority != null ? t.Priority.Name : null,
                CreatorId = t.CreatorId,
                CreatorName = t.Creator.Name,
                StoryPoints = t.StoryPoints,
                ProjectId = t.ProjectId,
                ParentTaskId = t.ParentTaskId,
                StartDate = t.StartDate,
                DueDate = t.DueDate,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                AssigneeIds = t.Assignments.Where(a => !a.IsDeleted).Select(a => a.AccountId).ToList()
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<TaskListItemResponse>(items, total, page, pageSize);
    }

    public async Task<TaskResponse> CreateAsync(int creatorId, CreateTaskRequest dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _projectAuth.CanManageTasksAsync(creatorId, dto.ProjectId, cancellationToken))
            throw new ForbiddenException("Only Admin, Project Manager, or Scrum Master can create tasks.");

        if (dto.StartDate == default)
            throw new ValidationException(nameof(dto.StartDate), "Start date is required.");

        if (!dto.StoryPoints.HasValue)
            throw new ValidationException(nameof(dto.StoryPoints), "Story points are required to calculate the due date.");

        if (!ValidFibonacciStoryPoints.Contains(dto.StoryPoints.Value))
            throw new ValidationException(nameof(dto.StoryPoints),
                "Story points must be a Fibonacci number (1, 2, 3, 5, 8, 13, 21).");

        var calculatedDueDate =
            BusinessDayHelper.CalculateDueDateFromStoryPoints(dto.StartDate, dto.StoryPoints.Value);

        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == dto.ProjectId, cancellationToken);

        if (project == null)
            throw new NotFoundException("Project not found.");

        if (calculatedDueDate > project.EndDate)
            throw new ValidationException(nameof(dto.StartDate),
                $"Calculated due date ({calculatedDueDate:yyyy-MM-dd}) exceeds " +
                $"the project end date ({project.EndDate:yyyy-MM-dd}).");

        if (dto.AssigneeIds.Distinct().Count() != dto.AssigneeIds.Count)
            throw new ValidationException(nameof(dto.AssigneeIds), "Duplicate assignee IDs are not allowed.");

        if (dto.AssigneeIds.Count > 0)
        {
            var validAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => dto.AssigneeIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            var invalidIds = dto.AssigneeIds.Except(validAccountIds).ToList();
            if (invalidIds.Count > 0)
                throw new ValidationException(nameof(dto.AssigneeIds),
                    $"The following assignee IDs do not exist: {string.Join(", ", invalidIds)}");
        }

        if (dto.PriorityId.HasValue)
        {
            var priorityExists =
                await _context.TaskPriorities.AnyAsync(p => p.Id == dto.PriorityId.Value, cancellationToken);
            if (!priorityExists)
                throw new ValidationException(nameof(dto.PriorityId), "Invalid PriorityId.");
        }

        var creator = await _context.Accounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == creatorId, cancellationToken);

        var creatorMember = await _context.ProjectMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.ProjectId == dto.ProjectId && m.AccountId == creatorId && !m.IsDeleted,
                cancellationToken);

        var creatorRole = creator.Role == AppRoles.Admin ? AppRoles.Admin : creatorMember?.Role ?? "";

        Dictionary<int, string?> assigneeEmails = new();
        if (dto.AssigneeIds.Count > 0)
        {
            assigneeEmails = await _context.Accounts
                .AsNoTracking()
                .Where(a => dto.AssigneeIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Email })
                .ToDictionaryAsync(a => a.Id, a => (string?)a.Email, cancellationToken);
        }

        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            PriorityId = dto.PriorityId,
            StatusId = 1,
            StartDate = dto.StartDate,
            DueDate = calculatedDueDate,
            StoryPoints = dto.StoryPoints,
            ProjectId = dto.ProjectId,
            ParentTaskId = dto.ParentTaskId,
            CreatorId = creatorId,
            CreatedAt = PhTime,
            UpdatedAt = PhTime
        };

        foreach (var accountId in dto.AssigneeIds)
        {
            task.Assignments.Add(new TaskAssignment
            {
                AccountId = accountId,
                AssignedById = creatorId,
                AssignedAt = PhTime,
                Task = task
            });

            _context.Notifications.Add(new Notification
            {
                AccountId = accountId,
                Message =
                    $"You have been assigned to task: '{task.Title}' in project '{project.Name}'.",
                ProjectId = task.ProjectId,
                TaskEntity = task,
                IsRead = false,
                CreatedAt = PhTime
            });
        }

        if (project.StatusId == 1)
        {
            project.StatusId = 2;
            project.UpdatedAt = PhTime;

            _context.AuditLogs.Add(new AuditLog
            {
                ProjectId = project.Id,
                TaskId = null,
                AccountId = creatorId,
                Action = "UPDATED",
                OldValue = "Not Started",
                NewValue = "Active",
                Note =
                    $"Project '{project.Name}' set to Active because a task was created by {creator.Name} ({creatorRole}).",
                CreatedAt = PhTime
            });
        }

        task.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            AccountId = creatorId,
            Action = "CREATED",
            NewValue = task.Title,
            Note = dto.ParentTaskId == null
                ? $"Task created '{task.Title}' by {creator.Name} ({creatorRole})."
                : $"Subtask created'{task.Title}' by {creator.Name} ({creatorRole}).",
            CreatedAt = PhTime
        });

        _context.Tasks.Add(task);

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var accountId in dto.AssigneeIds)
        {
            if (!assigneeEmails.TryGetValue(accountId, out var email) || email == null)
                continue;

            await _emailService.SendTaskAssignedAsync(email, task.Title);
        }

        return new TaskResponse
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            StatusId = task.StatusId,
            PriorityId = task.PriorityId,
            ProjectId = task.ProjectId,
            ParentTaskId = task.ParentTaskId,
            CreatorId = task.CreatorId,
            StoryPoints = task.StoryPoints,
            StartDate = task.StartDate,
            DueDate = task.DueDate,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt
        };
    }

    public async Task UpdateAsync(int updaterId, int taskId, UpdateTaskRequest dto,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        if (!await _projectAuth.CanManageTasksAsync(updaterId, task.ProjectId, cancellationToken))
            throw new ForbiddenException("Only Admin, Project Manager, or Scrum Master can update tasks.");

        if (dto.StartDate is null || dto.StartDate == default(DateTime))
            throw new ValidationException(nameof(dto.StartDate), "Start date is required.");

        var effectiveStoryPoints = dto.StoryPoints ?? task.StoryPoints;
        if (!effectiveStoryPoints.HasValue)
            throw new ValidationException(nameof(dto.StoryPoints),
                "Story points are required to recalculate the due date.");

        if (dto.StoryPoints.HasValue && !ValidFibonacciStoryPoints.Contains(dto.StoryPoints.Value))
            throw new ValidationException(nameof(dto.StoryPoints),
                "Story points must be a Fibonacci number (1, 2, 3, 5, 8, 13, 21).");

        var recalculatedDueDate =
            BusinessDayHelper.CalculateDueDateFromStoryPoints(dto.StartDate.Value, effectiveStoryPoints.Value);

        var project = await _context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project == null)
            throw new NotFoundException("Project not found.");

        if (recalculatedDueDate > project.EndDate)
            throw new ValidationException(nameof(dto.StartDate),
                $"Recalculated due date ({recalculatedDueDate:yyyy-MM-dd}) exceeds " +
                $"the project end date ({project.EndDate:yyyy-MM-dd}).");

        var changes = new List<string>();

        if (dto.Title != null && dto.Title != task.Title)
        {
            changes.Add($"Title: {task.Title} → {dto.Title}");
            task.Title = dto.Title;
        }
        if (dto.Description != null && dto.Description != task.Description)
        {
            changes.Add("Description updated");
            task.Description = dto.Description;
        }
        if (dto.StatusId.HasValue && dto.StatusId != task.StatusId)
        {
            var statusExists =
                await _context.TaskStatuses.AnyAsync(s => s.Id == dto.StatusId.Value, cancellationToken);
            if (!statusExists)
                throw new ValidationException(nameof(dto.StatusId), "Invalid StatusId.");

            changes.Add($"StatusId: {task.StatusId} → {dto.StatusId}");
            task.StatusId = dto.StatusId.Value;
        }
        if (dto.PriorityId.HasValue && dto.PriorityId != task.PriorityId)
        {
            var priorityExists =
                await _context.TaskPriorities.AnyAsync(p => p.Id == dto.PriorityId.Value, cancellationToken);
            if (!priorityExists)
                throw new ValidationException(nameof(dto.PriorityId), "Invalid PriorityId.");

            changes.Add($"PriorityId: {task.PriorityId} → {dto.PriorityId}");
            task.PriorityId = dto.PriorityId;
        }
        if (dto.StartDate.Value != task.StartDate)
        {
            changes.Add($"StartDate: {task.StartDate} → {dto.StartDate}");
            task.StartDate = dto.StartDate.Value;
        }
        if (recalculatedDueDate != task.DueDate)
        {
            changes.Add($"DueDate recalculated: {task.DueDate:yyyy-MM-dd HH:mm} → {recalculatedDueDate:yyyy-MM-dd HH:mm}");
            task.DueDate = recalculatedDueDate;
        }
        if (dto.StoryPoints.HasValue && dto.StoryPoints != task.StoryPoints)
        {
            changes.Add($"StoryPoints: {task.StoryPoints} → {dto.StoryPoints}");
            task.StoryPoints = dto.StoryPoints;
        }
        if (dto.ParentTaskId != task.ParentTaskId)
        {
            changes.Add($"ParentTaskId: {task.ParentTaskId} → {dto.ParentTaskId}");
            task.ParentTaskId = dto.ParentTaskId;
        }

        task.UpdatedAt = PhTime;

        if (changes.Count > 0)
        {
            var updater = await _context.Accounts
                .AsNoTracking()
                .FirstAsync(a => a.Id == updaterId, cancellationToken);
            var membership = await _projectAuth.GetMembershipAsync(updaterId, task.ProjectId, cancellationToken);
            var role = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

            _context.AuditLogs.Add(new AuditLog
            {
                ProjectId = task.ProjectId,
                TaskId = task.Id,
                AccountId = updaterId,
                Action = "UPDATED",
                NewValue = string.Join(", ", changes),
                Note = $"Task updated '{task.Title}' by {updater.Name} ({role}).",
                CreatedAt = PhTime
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(int requesterId, int taskId, UpdateTaskStatusRequest dto,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        if (!membership.IsMember && !membership.IsAdmin)
            throw new ForbiddenException("You are not a member of this project.");

        var isAssigned = await _context.TaskAssignments
            .AnyAsync(a => a.TaskId == taskId && a.AccountId == requesterId && !a.IsDeleted, cancellationToken);

        var newStatus = await _context.TaskStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == dto.StatusId, cancellationToken);
        if (newStatus == null)
            throw new ValidationException(nameof(dto.StatusId), "Invalid StatusId.");

        if (dto.StatusId == 4)
        {
            if (task.ParentTaskId == null && !membership.IsProjectManager && !membership.IsAdmin)
                throw new ForbiddenException("Only the Project Manager or Admin can mark a root task as Completed.");

            if (task.ParentTaskId != null && !membership.IsProjectManager && !membership.IsAdmin && !isAssigned)
                throw new ForbiddenException(
                    "Only an assigned member, Project Manager, or Admin can mark a subtask as Completed.");
        }

        if (dto.StatusId == 3 && !isAssigned && !membership.IsProjectManager && !membership.IsAdmin)
            throw new ForbiddenException("Only an assigned member can submit this task for review.");

        var oldStatus = await _context.TaskStatuses
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == task.StatusId, cancellationToken);
        var oldStatusName = oldStatus?.Name ?? task.StatusId.ToString();
        var newStatusName = newStatus.Name;

        task.StatusId = dto.StatusId;
        task.UpdatedAt = PhTime;

        var requester = await _context.Accounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == requesterId, cancellationToken);
        var requesterRole = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            AccountId = requesterId,
            Action = "UPDATED",
            OldValue = oldStatusName,
            NewValue = newStatusName,
            Note =
                $"Updated status of task '{task.Title}' to {newStatusName} by {requester.Name} ({requesterRole}).",
            CreatedAt = PhTime
        });

        var assignees = await _context.TaskAssignments
            .AsNoTracking()
            .Where(a => a.TaskId == taskId && !a.IsDeleted && a.AccountId != requesterId)
            .Join(_context.Accounts.AsNoTracking(),
                a => a.AccountId,
                acc => acc.Id,
                (a, acc) => new { acc.Id, acc.Email })
            .ToListAsync(cancellationToken);

        foreach (var assignee in assignees)
        {
            _context.Notifications.Add(new Notification
            {
                AccountId = assignee.Id,
                Message = $"Task '{task.Title}' status changed from '{oldStatusName}' to '{newStatusName}'.",
                ProjectId = task.ProjectId,
                TaskId = task.Id,
                IsRead = false,
                CreatedAt = PhTime
            });
        }

        if (dto.StatusId == 3 && !membership.IsProjectManager && !membership.IsAdmin)
        {
            var pmAccountId = await _context.ProjectMembers
                .AsNoTracking()
                .Where(m => m.ProjectId == task.ProjectId &&
                            (m.Role == AppRoles.ProjectManager || m.Role == AppRoles.PmScrumMaster) &&
                            !m.IsDeleted)
                .Select(m => (int?)m.AccountId)
                .FirstOrDefaultAsync(cancellationToken);

            if (pmAccountId.HasValue)
            {
                _context.Notifications.Add(new Notification
                {
                    AccountId = pmAccountId.Value,
                    Message = $"Task '{task.Title}' has been submitted for review by {requester.Name}.",
                    ProjectId = task.ProjectId,
                    TaskId = task.Id,
                    IsRead = false,
                    CreatedAt = PhTime
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var assignee in assignees)
        {
            if (string.IsNullOrEmpty(assignee.Email))
                continue;
            await _emailService.SendStatusChangedAsync(assignee.Email, task.Title, newStatusName);
        }
    }

    public async Task SoftDeleteAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        if (!await _projectAuth.CanManageTasksAsync(requesterId, task.ProjectId, cancellationToken))
            throw new ForbiddenException("You do not have permission to delete tasks.");

        var subtreeIds = await CollectSubtreeIdsAsync(taskId, includeDeleted: false, cancellationToken);

        var now = PhTime;

        await _context.Tasks
            .Where(t => subtreeIds.Contains(t.Id) && !t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, true)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        await _context.TaskAssignments
            .Where(a => subtreeIds.Contains(a.TaskId) && !a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, true)
                .SetProperty(a => a.DeletedAt, (DateTime?)now), cancellationToken);

        var deleter = await _context.Accounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == requesterId, cancellationToken);
        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        var role = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            AccountId = requesterId,
            Action = "DELETED",
            OldValue = task.Title,
            Note = $"Task '{task.Title}' and all subtasks deleted by {deleter.Name} ({role}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RestoreAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.IsDeleted, cancellationToken);
        if (task == null)
            throw new NotFoundException("Deleted task not found.");

        if (!await _projectAuth.CanManageTasksAsync(requesterId, task.ProjectId, cancellationToken))
            throw new ForbiddenException("You do not have permission to restore tasks.");

        var subtreeIds = await CollectSubtreeIdsAsync(taskId, includeDeleted: true, cancellationToken);

        var now = PhTime;

        var restoredCount = await _context.Tasks
            .Where(t => subtreeIds.Contains(t.Id) && t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, false)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        await _context.TaskAssignments
            .Where(a => subtreeIds.Contains(a.TaskId) && a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, false)
                .SetProperty(a => a.DeletedAt, (DateTime?)null), cancellationToken);

        var restorer = await _context.Accounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == requesterId, cancellationToken);
        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        var role = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            TaskId = task.Id,
            AccountId = requesterId,
            Action = "RESTORED",
            NewValue = task.Title,
            Note = $"Task and all subtasks reactivated by {restorer.Name} ({role}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return restoredCount;
    }

    public async Task<AssignTaskResponse> AssignAsync(int requesterId, int taskId, AssignTaskRequest dto,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && !t.IsDeleted, cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        if (!await _projectAuth.CanManageTasksAsync(requesterId, task.ProjectId, cancellationToken))
            throw new ForbiddenException("Only Admin, Project Manager, or Scrum Master can assign tasks.");

        var requestedIds = dto.AssigneeIds.Where(id => id > 0).Distinct().ToList();

        if (requestedIds.Count > 0)
        {
            var validAccountIds = await _context.Accounts
                .AsNoTracking()
                .Where(a => requestedIds.Contains(a.Id))
                .Select(a => a.Id)
                .ToListAsync(cancellationToken);

            var invalidIds = requestedIds.Except(validAccountIds).ToList();
            if (invalidIds.Count > 0)
                throw new ValidationException(nameof(dto.AssigneeIds),
                    $"The following assignee IDs do not exist: {string.Join(", ", invalidIds)}");
        }

        var existing = await _context.TaskAssignments
            .Where(a => a.TaskId == taskId)
            .ToListAsync(cancellationToken);

        var now = PhTime;

        var activeAccountIds = existing.Where(a => !a.IsDeleted).Select(a => a.AccountId).ToHashSet();
        var requestedSet = requestedIds.ToHashSet();

        var toAdd = requestedSet.Except(activeAccountIds).ToList();
        var toRemove = activeAccountIds.Except(requestedSet).ToList();

        foreach (var assignment in existing)
        {
            if (assignment.IsDeleted && requestedSet.Contains(assignment.AccountId))
            {
                assignment.IsDeleted = false;
                assignment.DeletedAt = null;
                assignment.AssignedById = requesterId;
                assignment.AssignedAt = now;
                toAdd.Remove(assignment.AccountId);
            }
            else if (!assignment.IsDeleted && !requestedSet.Contains(assignment.AccountId))
            {
                assignment.IsDeleted = true;
                assignment.DeletedAt = now;
            }
        }

        var project = await _context.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);

        var addedEmails = new Dictionary<int, string?>();
        if (toAdd.Count > 0)
        {
            addedEmails = await _context.Accounts
                .AsNoTracking()
                .Where(a => toAdd.Contains(a.Id))
                .Select(a => new { a.Id, a.Email })
                .ToDictionaryAsync(a => a.Id, a => (string?)a.Email, cancellationToken);
        }

        foreach (var accountId in toAdd)
        {
            _context.TaskAssignments.Add(new TaskAssignment
            {
                TaskId = taskId,
                AccountId = accountId,
                AssignedById = requesterId,
                AssignedAt = now
            });

            _context.Notifications.Add(new Notification
            {
                AccountId = accountId,
                Message = $"You have been assigned to task: '{task.Title}' in project '{project?.Name}'.",
                ProjectId = task.ProjectId,
                TaskId = task.Id,
                IsRead = false,
                CreatedAt = now
            });
        }

        task.UpdatedAt = now;

        var assigner = await _context.Accounts
            .AsNoTracking()
            .FirstAsync(a => a.Id == requesterId, cancellationToken);
        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        var assignerRole = membership.IsAdmin ? AppRoles.Admin : membership.MemberRole ?? "Unknown";

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            TaskId = taskId,
            AccountId = requesterId,
            Action = "UPDATED",
            NewValue = string.Join(", ", requestedIds),
            Note = $"Task '{task.Title}' assigned by {assigner.Name} ({assignerRole}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var accountId in toAdd)
        {
            if (!addedEmails.TryGetValue(accountId, out var email) || string.IsNullOrEmpty(email))
                continue;
            await _emailService.SendTaskAssignedAsync(email, task.Title);
        }

        var warnings = await CheckWorkloadWarningsAsync(taskId, requestedIds, cancellationToken);

        return new AssignTaskResponse
        {
            Message = warnings.Count > 0
                ? "Task assigned successfully, but some assignees are overloaded."
                : "Task assigned successfully.",
            Added = toAdd,
            Removed = toRemove,
            Warnings = warnings
        };
    }

    public async Task<IReadOnlyList<ProjectTaskStatItem>> GetProjectStatsBatchAsync(int requesterId,
        IReadOnlyList<int> projectIds, CancellationToken cancellationToken = default)
    {
        var distinct = projectIds.Where(id => id > 0).Distinct().ToList();
        var results = new List<ProjectTaskStatItem>(distinct.Count);

        foreach (var projectId in distinct)
        {
            var visibility = await _projectAuth.GetTaskVisibilityAsync(requesterId, projectId, cancellationToken);
            if (visibility == TaskVisibility.None)
            {
                results.Add(new ProjectTaskStatItem(projectId, 0, 0));
                continue;
            }

            IQueryable<TaskItem> query = _context.Tasks
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted);

            if (visibility == TaskVisibility.OnlyAssigned)
                query = query.Where(t =>
                    t.Assignments.Any(a => a.AccountId == requesterId && !a.IsDeleted));

            var total = await query.CountAsync(cancellationToken);
            var completed = await query.CountAsync(t => t.StatusId == 4, cancellationToken);

            results.Add(new ProjectTaskStatItem(projectId, total, completed));
        }

        return results;
    }

    public async Task<CheckAssigneeWorkloadResult> CheckAssigneeWorkloadAsync(DateTime startDate, int storyPoints,
        IReadOnlyList<int> assigneeIds, CancellationToken cancellationToken = default)
    {
        if (!ValidFibonacciStoryPoints.Contains(storyPoints))
            throw new ValidationException(nameof(storyPoints),
                "Story points must be a Fibonacci number (1, 2, 3, 5, 8, 13, 21).");

        if (startDate == default)
            throw new ValidationException(nameof(startDate), "Start date is required.");

        var projectedDueDate = BusinessDayHelper.CalculateDueDateFromStoryPoints(startDate, storyPoints);
        var newTaskHours = BusinessDayHelper.GetHoursForStoryPoints(storyPoints);

        var assigneeIdSet = assigneeIds.Where(id => id > 0).Distinct().ToList();

        if (assigneeIdSet.Count == 0)
            return new CheckAssigneeWorkloadResult(
                startDate.ToString("yyyy-MM-dd HH:mm"),
                projectedDueDate.ToString("yyyy-MM-dd HH:mm"),
                storyPoints,
                Array.Empty<AssigneeWorkloadWarningDto>());

        var accountNames = await _context.Accounts
            .AsNoTracking()
            .Where(a => assigneeIdSet.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var overlappingRows = await _context.TaskAssignments
            .AsNoTracking()
            .Where(a =>
                assigneeIdSet.Contains(a.AccountId) &&
                !a.IsDeleted &&
                !a.Task.IsDeleted &&
                a.Task.StoryPoints != null &&
                a.Task.StartDate.HasValue &&
                a.Task.DueDate.HasValue &&
                a.Task.StartDate.Value.Date <= projectedDueDate.Date &&
                a.Task.DueDate.Value.Date >= startDate.Date)
            .Select(a => new { a.AccountId, StoryPoints = a.Task.StoryPoints!.Value })
            .ToListAsync(cancellationToken);

        var existingHoursByAccount = overlappingRows
            .GroupBy(x => x.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => BusinessDayHelper.GetHoursForStoryPoints(x.StoryPoints)));

        var warnings = new List<AssigneeWorkloadWarningDto>();

        foreach (var accountId in assigneeIdSet)
        {
            var existingHours = existingHoursByAccount.TryGetValue(accountId, out var val) ? val : 0;
            var totalHours = existingHours + newTaskHours;
            if (totalHours <= 8)
                continue;

            accountNames.TryGetValue(accountId, out var accountName);
            warnings.Add(new AssigneeWorkloadWarningDto(
                accountId,
                accountName,
                existingHours,
                newTaskHours,
                totalHours,
                8,
                totalHours - 8,
                startDate.ToString("yyyy-MM-dd HH:mm"),
                projectedDueDate.ToString("yyyy-MM-dd HH:mm"),
                $"{accountName} is overloaded by {totalHours - 8}h " +
                $"({totalHours}h total / 8h daily capacity) " +
                $"during {startDate:yyyy-MM-dd} to {projectedDueDate:yyyy-MM-dd}."));
        }

        return new CheckAssigneeWorkloadResult(
            startDate.ToString("yyyy-MM-dd HH:mm"),
            projectedDueDate.ToString("yyyy-MM-dd HH:mm"),
            storyPoints,
            warnings);
    }

    public async Task<IReadOnlyList<TaskCatalogItem>> GetStatusesCatalogAsync(
        CancellationToken cancellationToken = default)
        => await _context.TaskStatuses
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .Select(t => new TaskCatalogItem(t.Id, t.Name, t.Description, t.IsActive, t.CreatedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskCatalogItem>> GetPrioritiesCatalogAsync(
        CancellationToken cancellationToken = default)
        => await _context.TaskPriorities
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Id)
            .Select(t => new TaskCatalogItem(t.Id, t.Name, t.Description, t.IsActive, t.CreatedAt))
            .ToListAsync(cancellationToken);

    private async Task<List<int>> CollectSubtreeIdsAsync(int rootId, bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var allIds = new List<int> { rootId };
        var frontier = new List<int> { rootId };

        while (frontier.Count > 0)
        {
            var children = await _context.Tasks
                .AsNoTracking()
                .Where(t => t.ParentTaskId.HasValue && frontier.Contains(t.ParentTaskId.Value)
                            && (includeDeleted ? t.IsDeleted : !t.IsDeleted))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            if (children.Count == 0)
                break;

            allIds.AddRange(children);
            frontier = children;
        }

        return allIds;
    }

    private async Task<IReadOnlyList<TaskWorkloadWarning>> CheckWorkloadWarningsAsync(int taskId,
        IReadOnlyList<int> assigneeIds, CancellationToken cancellationToken)
    {
        var warnings = new List<TaskWorkloadWarning>();

        var assigneeIdSet = assigneeIds.Where(id => id > 0).Distinct().ToList();
        if (assigneeIdSet.Count == 0)
            return warnings;

        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.StartDate, t.DueDate, t.StoryPoints })
            .FirstOrDefaultAsync(cancellationToken);

        if (task == null || !task.StoryPoints.HasValue || !task.StartDate.HasValue || !task.DueDate.HasValue)
            return warnings;

        var accountNames = await _context.Accounts
            .AsNoTracking()
            .Where(a => assigneeIdSet.Contains(a.Id))
            .Select(a => new { a.Id, a.Name })
            .ToDictionaryAsync(a => a.Id, a => a.Name, cancellationToken);

        var overlapping = await _context.TaskAssignments
            .AsNoTracking()
            .Where(a =>
                assigneeIdSet.Contains(a.AccountId) &&
                !a.IsDeleted &&
                !a.Task.IsDeleted &&
                a.TaskId != taskId &&
                a.Task.StoryPoints != null &&
                a.Task.StartDate.HasValue &&
                a.Task.DueDate.HasValue &&
                a.Task.StartDate!.Value.Date <= task.DueDate!.Value.Date &&
                a.Task.DueDate!.Value.Date >= task.StartDate!.Value.Date)
            .Select(a => new { a.AccountId, StoryPoints = a.Task.StoryPoints!.Value })
            .ToListAsync(cancellationToken);

        var existingHoursByAccount = overlapping
            .GroupBy(x => x.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => BusinessDayHelper.GetHoursForStoryPoints(x.StoryPoints))
            );

        var newTaskHours = BusinessDayHelper.GetHoursForStoryPoints(task.StoryPoints.Value);

        foreach (var accountId in assigneeIdSet)
        {
            var existingHours = existingHoursByAccount.TryGetValue(accountId, out var val) ? val : 0;
            var totalHours = existingHours + newTaskHours;
            if (totalHours <= 8)
                continue;

            accountNames.TryGetValue(accountId, out var accountName);
            warnings.Add(new TaskWorkloadWarning
            {
                AccountId = accountId,
                AccountName = accountName,
                TotalHours = totalHours,
                NewTaskHours = newTaskHours,
                ExistingHours = existingHours,
                Capacity = 8,
                OverloadBy = totalHours - 8,
                Message = $"{accountName} is overloaded by {totalHours - 8}h " +
                          $"({totalHours}h total / 8h daily capacity) " +
                          $"during {task.StartDate:yyyy-MM-dd} to {task.DueDate:yyyy-MM-dd}."
            });
        }

        return warnings;
    }
}
