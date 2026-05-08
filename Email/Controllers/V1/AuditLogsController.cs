using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.AuditLog;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController : ApiControllerBase
{
    private readonly IAuditLogsService _service;

    public AuditLogsController(IAuditLogsService service)
    {
        _service = service;
    }

    /// <summary>
    /// Paginated, filtered audit log feed. Admin-only (resolved from bearer token).
    /// Replaces 5 legacy endpoints (GetActionLogs, GetLogsByDateRange, GetLogsByAction, GetTaskLogs, GetUserLogs)
    /// + GetLoginLogoutLogs via the <c>kind</c> discriminator.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditLogPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuditLogPageResponse>> List(
        [FromQuery] AuditLogKind kind = AuditLogKind.All,
        [FromQuery] int? userId = null,
        [FromQuery] int? taskId = null,
        [FromQuery] int? projectId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? accountName = null,
        [FromQuery] string? accountEmail = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var filter = new AuditLogFilter
        {
            UserId = userId,
            TaskId = taskId,
            ProjectId = projectId,
            Action = string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
            AccountName = string.IsNullOrWhiteSpace(accountName) ? null : accountName.Trim(),
            AccountEmail = string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail.Trim(),
            From = from,
            To = to,
        };

        return Ok(await _service.ListAsync(CurrentAccount.Id, kind, filter, page, pageSize, cancellationToken));
    }

    /// <summary>
    /// Streams an Excel or PDF export. Replaces 4 legacy export endpoints
    /// (ExportActionLogsExcel/Pdf + ExportLoginLogoutExcel/Pdf) via <c>kind</c> + <c>format</c>.
    /// </summary>
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export(
        [FromQuery] AuditLogKind kind,
        [FromQuery] AuditLogExportFormat format,
        [FromQuery] int? userId = null,
        [FromQuery] int? taskId = null,
        [FromQuery] int? projectId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? accountName = null,
        [FromQuery] string? accountEmail = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new AuditLogFilter
        {
            UserId = userId,
            TaskId = taskId,
            ProjectId = projectId,
            Action = string.IsNullOrWhiteSpace(action) ? null : action.Trim(),
            AccountName = string.IsNullOrWhiteSpace(accountName) ? null : accountName.Trim(),
            AccountEmail = string.IsNullOrWhiteSpace(accountEmail) ? null : accountEmail.Trim(),
            From = from,
            To = to,
        };

        var result = await _service.ExportAsync(CurrentAccount.Id, kind, format, filter, cancellationToken);
        return File(result.Bytes, result.ContentType, result.FileName);
    }
}
