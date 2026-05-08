namespace TaskManagement.DTOs.Task;

public sealed record ProjectStatsBatchResponse(IReadOnlyList<ProjectTaskStatItem> Items);
