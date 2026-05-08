using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Task;

public sealed record CreateTaskRequest
{
    [Required]
    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public int? PriorityId { get; init; }

    public int? StoryPoints { get; init; }

    [Required]
    public int ProjectId { get; init; }

    public int? ParentTaskId { get; init; }

    [Required]
    public DateTime StartDate { get; init; }

    public List<int> AssigneeIds { get; init; } = new();
}
