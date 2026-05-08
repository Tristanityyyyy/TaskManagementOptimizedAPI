namespace TaskManagement.DTOs.Task;

public sealed record TaskResponse
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public int? PriorityId { get; init; }
    public int ProjectId { get; init; }
    public int? ParentTaskId { get; init; }
    public int CreatorId { get; init; }
    public int? StoryPoints { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
