namespace TaskManagement.DTOs.Notification;

public sealed record NotificationResponse
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public int? ProjectId { get; init; }
    public int? TaskId { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsRead { get; init; }
    public DateTime CreatedAt { get; init; }
}
