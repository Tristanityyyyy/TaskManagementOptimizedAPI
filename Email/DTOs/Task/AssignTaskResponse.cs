namespace TaskManagement.DTOs.Task;

public sealed record AssignTaskResponse
{
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<int> Added { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> Removed { get; init; } = Array.Empty<int>();
    public IReadOnlyList<TaskWorkloadWarning> Warnings { get; init; } = Array.Empty<TaskWorkloadWarning>();
}

public sealed record TaskWorkloadWarning
{
    public int AccountId { get; init; }
    public string? AccountName { get; init; }
    public double TotalHours { get; init; }
    public double NewTaskHours { get; init; }
    public double ExistingHours { get; init; }
    public double Capacity { get; init; }
    public double OverloadBy { get; init; }
    public string Message { get; init; } = string.Empty;
}
