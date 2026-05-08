namespace TaskManagement.DTOs.Dashboard;

/// <summary>
/// Flat task projection for the calendar grid. Skips description / story points / hierarchy
/// since the calendar only cares about scheduling. Massively smaller payload than walking
/// <c>MyProjectsAndTasks</c> just to extract due dates.
/// </summary>
public sealed record CalendarTaskResponse
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int? PriorityId { get; init; }
    public string PriorityName { get; init; } = string.Empty;
    public DateTime? StartDate { get; init; }
    public DateTime? DueDate { get; init; }
    public int SubtaskCount { get; init; }
    public IReadOnlyList<int> AssigneeIds { get; init; } = Array.Empty<int>();
}
