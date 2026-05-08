namespace TaskManagement.DTOs.Task;

public sealed record UpdateTaskRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public int? StatusId { get; init; }
    public int? PriorityId { get; init; }
    public int? StoryPoints { get; init; }
    public DateTime? StartDate { get; init; }
    public int? ParentTaskId { get; init; }
}
