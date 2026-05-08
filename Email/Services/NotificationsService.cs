using Microsoft.EntityFrameworkCore;
using TaskManagement.Data;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Notification;
using TaskManagement.Exceptions;

namespace TaskManagement.Services;

public sealed class NotificationsService : INotificationsService
{
    private const int MaxPageSize = 100;
    private const int MaxBatchSize = 500;

    private readonly AccountDbContext _context;

    public NotificationsService(AccountDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<NotificationResponse>> ListAsync(int requesterId, bool unreadOnly, int page,
        int pageSize, CancellationToken cancellationToken = default)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize <= 0 ? 20 : Math.Min(pageSize, MaxPageSize);

        var query = _context.Notifications
            .AsNoTracking()
            .Where(n => n.AccountId == requesterId);

        if (unreadOnly)
            query = query.Where(n => !n.IsRead);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .Select(n => new NotificationResponse
            {
                Id = n.Id,
                AccountId = n.AccountId,
                ProjectId = n.ProjectId,
                TaskId = n.TaskId,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<NotificationResponse>(items, total, safePage, safeSize);
    }

    public async Task<UnreadCountResponse> GetUnreadCountAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        var count = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.AccountId == requesterId && !n.IsRead)
            .CountAsync(cancellationToken);

        return new UnreadCountResponse(count);
    }

    public Task<int> MarkAsReadAsync(int requesterId, int id, CancellationToken cancellationToken = default)
        => SetReadStateAsync(requesterId, id, isRead: true, cancellationToken);

    public Task<int> MarkAsUnreadAsync(int requesterId, int id, CancellationToken cancellationToken = default)
        => SetReadStateAsync(requesterId, id, isRead: false, cancellationToken);

    public async Task<int> MarkAllAsReadAsync(int requesterId, CancellationToken cancellationToken = default)
    {
        return await _context.Notifications
            .Where(n => n.AccountId == requesterId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);
    }

    public async Task<int> MarkManyAsReadAsync(int requesterId, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var safeIds = NormalizeIds(ids);
        if (safeIds.Count == 0)
            return 0;

        return await _context.Notifications
            .Where(n => n.AccountId == requesterId && safeIds.Contains(n.Id) && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);
    }

    public async Task<int> DeleteAsync(int requesterId, int id, CancellationToken cancellationToken = default)
    {
        var affected = await _context.Notifications
            .Where(n => n.Id == id && n.AccountId == requesterId)
            .ExecuteDeleteAsync(cancellationToken);

        if (affected == 0)
            throw new NotFoundException("Notification not found.");

        return affected;
    }

    public async Task<int> DeleteManyAsync(int requesterId, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var safeIds = NormalizeIds(ids);
        if (safeIds.Count == 0)
            return 0;

        return await _context.Notifications
            .Where(n => n.AccountId == requesterId && safeIds.Contains(n.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task<int> SetReadStateAsync(int requesterId, int id, bool isRead,
        CancellationToken cancellationToken)
    {
        var affected = await _context.Notifications
            .Where(n => n.Id == id && n.AccountId == requesterId && n.IsRead != isRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, isRead), cancellationToken);

        if (affected == 0)
        {
            // Either not found, not owned by caller, or already in the desired state.
            // Check ownership so we throw a meaningful 404 (not silently 0) when applicable.
            var exists = await _context.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.Id == id && n.AccountId == requesterId, cancellationToken);
            if (!exists)
                throw new NotFoundException("Notification not found.");
        }

        return affected;
    }

    private static List<int> NormalizeIds(IReadOnlyCollection<int> ids)
    {
        if (ids == null || ids.Count == 0)
            return new List<int>();

        var distinct = ids.Where(x => x > 0).Distinct().Take(MaxBatchSize).ToList();
        if (ids.Count > MaxBatchSize)
            throw new ValidationException(nameof(ids), $"At most {MaxBatchSize} ids can be processed per request.");
        return distinct;
    }
}
