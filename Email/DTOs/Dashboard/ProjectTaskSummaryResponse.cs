namespace TaskManagement.DTOs.Dashboard;

/// <summary>
/// Pie-chart payload for a single project. Cleaner shape than the legacy
/// <c>{ completed: {count, percentage}, forReview: {...} }</c> nested objects;
/// frontend can iterate the breakdown directly.
/// </summary>
public sealed record ProjectTaskSummaryResponse
{
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public int TotalTasks { get; init; }
    public double CompletionPercentage { get; init; }
    public IReadOnlyList<ProjectTaskSummaryItem> Breakdown { get; init; } = Array.Empty<ProjectTaskSummaryItem>();
}

public sealed record ProjectTaskSummaryItem
{
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int Count { get; init; }
    public double Percentage { get; init; }
}
