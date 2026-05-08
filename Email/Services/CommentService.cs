using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Comment;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class CommentService : ICommentService
{
    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IProjectAuthService _projectAuth;

    public CommentService(AccountDbContext context, IProjectAuthService projectAuth, IEmailService emailService)
    {
        _context = context;
        _projectAuth = projectAuth;
        _emailService = emailService;
    }

    public async Task<IReadOnlyList<CommentResponse>> ListByTaskAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new { t.Id, t.ProjectId })
            .FirstOrDefaultAsync(cancellationToken);

        if (task == null)
            throw new NotFoundException("Task not found.");

        var visibility = await _projectAuth.GetTaskVisibilityAsync(requesterId, task.ProjectId, cancellationToken);
        if (visibility == TaskVisibility.None)
            throw new ForbiddenException("You are not a member of this project.");

        if (visibility == TaskVisibility.OnlyAssigned)
        {
            var isAssigned = await _context.TaskAssignments
                .AsNoTracking()
                .AnyAsync(a => a.TaskId == taskId && a.AccountId == requesterId && !a.IsDeleted,
                    cancellationToken);
            if (!isAssigned)
                throw new ForbiddenException("You are not assigned to this task.");
        }

        return await _context.TaskComments
            .AsNoTracking()
            .Where(c => c.TaskId == taskId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CommentResponse
            {
                Id = c.Id,
                TaskId = c.TaskId,
                AccountId = c.AccountId,
                AccountName = _context.Accounts
                    .Where(a => a.Id == c.AccountId)
                    .Select(a => a.Name)
                    .FirstOrDefault() ?? "User",
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CommentResponse> CreateAsync(int requesterId, int taskId, CreateCommentRequest dto,
        CancellationToken cancellationToken = default)
    {
        var content = (dto.Content ?? string.Empty).Trim();
        if (content.Length == 0)
            throw new ValidationException(nameof(dto.Content), "Comment content is required.");

        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == taskId && !t.IsDeleted)
            .Select(t => new { t.Id, t.ProjectId, t.Title, t.CreatorId })
            .FirstOrDefaultAsync(cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        if (!membership.IsMember && !membership.IsAdmin)
            throw new ForbiddenException("You are not a member of this project.");

        var commenter = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name })
            .FirstAsync(cancellationToken);

        var now = PhTime;
        var comment = new TaskComment
        {
            TaskId = taskId,
            AccountId = requesterId,
            Content = content,
            CreatedAt = now,
            UpdatedAt = now
        };
        _context.TaskComments.Add(comment);

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = task.ProjectId,
            TaskId = taskId,
            AccountId = requesterId,
            Action = "CREATED",
            NewValue = content,
            Note = $"User {commenter.Name} leaves a comment on task {task.Title}.",
            CreatedAt = now
        });

        var memberAccountIds = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == task.ProjectId && m.AccountId != requesterId && !m.IsDeleted)
            .Select(m => m.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var accountId in memberAccountIds)
        {
            _context.Notifications.Add(new Notification
            {
                AccountId = accountId,
                ProjectId = task.ProjectId,
                TaskId = taskId,
                Message = $"{commenter.Name} commented on task \"{task.Title}\"",
                IsRead = false,
                CreatedAt = now
            });
        }

        var recipientIds = await _context.TaskAssignments
            .AsNoTracking()
            .Where(a => a.TaskId == taskId && !a.IsDeleted && a.AccountId != requesterId)
            .Select(a => a.AccountId)
            .ToListAsync(cancellationToken);

        if (task.CreatorId != requesterId && !recipientIds.Contains(task.CreatorId))
            recipientIds.Add(task.CreatorId);

        var recipients = recipientIds.Count > 0
            ? await _context.Accounts
                .AsNoTracking()
                .Where(a => recipientIds.Contains(a.Id))
                .Select(a => new { a.Email, a.Name })
                .ToListAsync(cancellationToken)
            : new();

        await _context.SaveChangesAsync(cancellationToken);

        foreach (var recipient in recipients)
        {
            if (string.IsNullOrEmpty(recipient.Email))
                continue;
            await _emailService.SendCommentNotificationAsync(
                recipient.Email,
                recipient.Name,
                commenter.Name,
                task.Title,
                content);
        }

        return new CommentResponse
        {
            Id = comment.Id,
            TaskId = comment.TaskId,
            AccountId = comment.AccountId,
            AccountName = commenter.Name,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt
        };
    }

    public async Task UpdateAsync(int requesterId, int commentId, UpdateCommentRequest dto,
        CancellationToken cancellationToken = default)
    {
        var content = (dto.Content ?? string.Empty).Trim();
        if (content.Length == 0)
            throw new ValidationException(nameof(dto.Content), "Comment content is required.");

        var comment = await _context.TaskComments
            .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, cancellationToken);
        if (comment == null)
            throw new NotFoundException("Comment not found.");

        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == comment.TaskId)
            .Select(t => new { t.ProjectId, t.Title })
            .FirstOrDefaultAsync(cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);
        if (comment.AccountId != requesterId && !membership.IsAdmin)
            throw new ForbiddenException("You can only edit your own comments.");

        comment.Content = content;
        comment.UpdatedAt = PhTime;

        var commenterName = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Name)
            .FirstAsync(cancellationToken);

        var memberAccountIds = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == task.ProjectId && m.AccountId != requesterId && !m.IsDeleted)
            .Select(m => m.AccountId)
            .ToListAsync(cancellationToken);

        foreach (var accountId in memberAccountIds)
        {
            _context.Notifications.Add(new Notification
            {
                AccountId = accountId,
                ProjectId = task.ProjectId,
                TaskId = comment.TaskId,
                Message = $"{commenterName} updated a comment on task \"{task.Title}\"",
                IsRead = false,
                CreatedAt = PhTime
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(int requesterId, int commentId,
        CancellationToken cancellationToken = default)
    {
        var comment = await _context.TaskComments
            .FirstOrDefaultAsync(c => c.Id == commentId && !c.IsDeleted, cancellationToken);
        if (comment == null)
            throw new NotFoundException("Comment not found.");

        var task = await _context.Tasks
            .AsNoTracking()
            .Where(t => t.Id == comment.TaskId)
            .Select(t => new { t.ProjectId })
            .FirstOrDefaultAsync(cancellationToken);
        if (task == null)
            throw new NotFoundException("Task not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, task.ProjectId, cancellationToken);

        var canManage = membership.IsAdmin || membership.IsProjectManager || membership.IsScrumMaster;

        if (comment.AccountId != requesterId && !canManage)
            throw new ForbiddenException("You do not have permission to delete this comment.");

        comment.IsDeleted = true;
        comment.UpdatedAt = PhTime;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
