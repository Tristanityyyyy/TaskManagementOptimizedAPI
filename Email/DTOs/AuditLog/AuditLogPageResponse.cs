namespace TaskManagement.DTOs.AuditLog;

public sealed record AuditLogPageResponse
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
    public IReadOnlyList<AuditLogResponse> Items { get; init; } = Array.Empty<AuditLogResponse>();
}
