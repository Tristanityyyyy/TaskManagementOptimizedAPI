using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Notification;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/notifications")]
public sealed class NotificationsController : ApiControllerBase
{
    private readonly INotificationsService _notifications;

    public NotificationsController(INotificationsService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet(Name = nameof(List))]
    [ProducesResponseType(typeof(PagedResult<NotificationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<NotificationResponse>>> List(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _notifications.ListAsync(CurrentAccount.Id, unreadOnly, page, pageSize, cancellationToken));

    [HttpGet("unread/count")]
    [ProducesResponseType(typeof(UnreadCountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(
        CancellationToken cancellationToken = default)
        => Ok(await _notifications.GetUnreadCountAsync(CurrentAccount.Id, cancellationToken));

    [HttpPatch("{id:int}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken = default)
    {
        await _notifications.MarkAsReadAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:int}/unread")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkUnread(int id, CancellationToken cancellationToken = default)
    {
        await _notifications.MarkAsUnreadAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("read-all")]
    [ProducesResponseType(typeof(AffectedRowsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AffectedRowsResponse>> MarkAllRead(CancellationToken cancellationToken = default)
    {
        var affected = await _notifications.MarkAllAsReadAsync(CurrentAccount.Id, cancellationToken);
        return Ok(new AffectedRowsResponse(affected));
    }

    [HttpPatch("read")]
    [ProducesResponseType(typeof(AffectedRowsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AffectedRowsResponse>> MarkManyRead(
        [FromBody] NotificationIdsRequest? dto,
        CancellationToken cancellationToken = default)
    {
        var ids = dto?.Ids ?? new List<int>();
        var affected = await _notifications.MarkManyAsReadAsync(CurrentAccount.Id, ids, cancellationToken);
        return Ok(new AffectedRowsResponse(affected));
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        await _notifications.DeleteAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("delete")]
    [ProducesResponseType(typeof(AffectedRowsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AffectedRowsResponse>> DeleteMany(
        [FromBody] NotificationIdsRequest? dto,
        CancellationToken cancellationToken = default)
    {
        var ids = dto?.Ids ?? new List<int>();
        var affected = await _notifications.DeleteManyAsync(CurrentAccount.Id, ids, cancellationToken);
        return Ok(new AffectedRowsResponse(affected));
    }
}
