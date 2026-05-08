namespace TaskManagement.DTOs.AuditLog;

/// <summary>
/// Discriminator for the kind of audit logs to return.
/// <list type="bullet">
///   <item><description><c>Action</c>: CRUD-style entries (CREATED, UPDATED, DELETED, RESTORED).</description></item>
///   <item><description><c>LoginLogout</c>: authentication entries (Logged in, Logged out).</description></item>
///   <item><description><c>All</c>: no kind filter (used for free-form filtered queries by taskId/userId/etc.).</description></item>
/// </list>
/// </summary>
public enum AuditLogKind
{
    All = 0,
    Action = 1,
    LoginLogout = 2,
}

public enum AuditLogExportFormat
{
    Excel = 1,
    Pdf = 2,
}

public sealed record AuditLogFilter
{
    public int? UserId { get; init; }
    public int? TaskId { get; init; }
    public int? ProjectId { get; init; }
    public string? AccountName { get; init; }
    public string? AccountEmail { get; init; }
    public string? Action { get; init; }
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
}
