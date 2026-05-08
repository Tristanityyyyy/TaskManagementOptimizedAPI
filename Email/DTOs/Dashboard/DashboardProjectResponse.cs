namespace TaskManagement.DTOs.Dashboard;

/// <summary>
/// Project + task tree for the dashboard / calendar landing pages. Replaces
/// <c>GET /api/Dashboard/MyProjectsAndTasks</c>.
/// </summary>
public sealed record DashboardProjectResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public IReadOnlyList<DashboardTaskResponse> Tasks { get; init; } = Array.Empty<DashboardTaskResponse>();
}

public sealed record DashboardTaskResponse
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int? PriorityId { get; init; }
    public string PriorityName { get; init; } = string.Empty;
    public int? StoryPoints { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public IReadOnlyList<int> AssigneeIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<DashboardTaskResponse> Subtasks { get; init; } = Array.Empty<DashboardTaskResponse>();
}
