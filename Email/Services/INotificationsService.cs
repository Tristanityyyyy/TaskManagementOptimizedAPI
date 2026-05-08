using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Notification;

namespace TaskManagement.Services;

public interface INotificationsService
{
    Task<PagedResult<NotificationResponse>> ListAsync(int requesterId, bool unreadOnly, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<UnreadCountResponse> GetUnreadCountAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<int> MarkAsReadAsync(int requesterId, int id,
        CancellationToken cancellationToken = default);

    Task<int> MarkAsUnreadAsync(int requesterId, int id,
        CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<int> MarkManyAsReadAsync(int requesterId, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAsync(int requesterId, int id,
        CancellationToken cancellationToken = default);

    Task<int> DeleteManyAsync(int requesterId, IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);
}
