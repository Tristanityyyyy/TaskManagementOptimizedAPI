namespace TaskManagement.DTOs.Task;

public sealed record CheckAssigneeWorkloadResult(
    string ProjectedStartDate,
    string ProjectedDueDate,
    int StoryPoints,
    IReadOnlyList<AssigneeWorkloadWarningDto> Warnings);
