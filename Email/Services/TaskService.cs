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
}
