namespace TaskManagement.DTOs.Task;

public sealed record AssignTaskRequest
{
    public List<int> AssigneeIds { get; init; } = new();
}
