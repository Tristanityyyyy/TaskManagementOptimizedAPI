namespace TaskManagement.DTOs.Task;

public sealed record TaskListItemResponse
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int? PriorityId { get; init; }
    public string? PriorityName { get; init; }
    public int? StoryPoints { get; init; }
    public int CreatorId { get; init; }
    public string? CreatorName { get; init; }
    public int ProjectId { get; init; }
    public int? ParentTaskId { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public List<int> AssigneeIds { get; init; } = new();
}
