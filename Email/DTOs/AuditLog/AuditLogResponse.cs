namespace TaskManagement.DTOs.AuditLog;

public sealed record AuditLogResponse
{
    public int Id { get; init; }
    public int? TaskId { get; init; }
    public int? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public string? ProjectRole { get; init; }
    public int AccountId { get; init; }
    public string? AccountName { get; init; }
    public string? AccountEmail { get; init; }
    public string Action { get; init; } = string.Empty;
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }
}
