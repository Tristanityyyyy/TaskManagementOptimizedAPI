using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Task;

public sealed record UpdateTaskStatusRequest
{
    [Required]
    public int StatusId { get; init; }
}
