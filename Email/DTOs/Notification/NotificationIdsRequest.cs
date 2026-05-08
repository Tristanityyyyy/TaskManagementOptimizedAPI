using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Notification;

public sealed record NotificationIdsRequest
{
    [Required]
    public List<int> Ids { get; init; } = new();
}
