namespace TaskManagement.DTOs.Task;

public sealed record AssigneeWorkloadWarningDto(
    int AccountId,
    string? AccountName,
    double ExistingHours,
    double NewTaskHours,
    double TotalHours,
    int Capacity,
    double OverloadBy,
    string ProjectedStartDate,
    string ProjectedDueDate,
    string Message);
