namespace TaskManagement.DTOs.Project;

public sealed record ProjectResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int CreatedById { get; init; }
    public int ProjectManagerId { get; init; }
    public int? ScrumMasterId { get; init; }
    public IReadOnlyList<int> AssigneeIds { get; init; } = Array.Empty<int>();
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
