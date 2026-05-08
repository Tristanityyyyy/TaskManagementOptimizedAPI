using TaskManagement.DTOs.AuditLog;

namespace TaskManagement.Services;

public interface IAuditLogsService
{
    Task<AuditLogPageResponse> ListAsync(int requesterId,
        AuditLogKind kind,
        AuditLogFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AuditLogExportResult> ExportAsync(int requesterId,
        AuditLogKind kind,
        AuditLogExportFormat format,
        AuditLogFilter filter,
        CancellationToken cancellationToken = default);
}

public sealed record AuditLogExportResult(
    byte[] Bytes,
    string ContentType,
    string FileName);
