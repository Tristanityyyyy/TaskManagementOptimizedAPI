namespace TaskManagement.DTOs.Project;

public sealed record UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? StatusId { get; init; }
    public int? ProjectManagerId { get; init; }
    public int? ScrumMasterId { get; init; }

    /// <summary>Members to set (excluding PM/SM); when null the member list is left untouched.</summary>
    public List<int>? AssigneeIds { get; init; }

    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}
