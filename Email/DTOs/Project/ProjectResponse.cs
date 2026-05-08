namespace TaskManagement.DTOs.Project;

public sealed record ProjectResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int StatusId { get; init; }
    public string StatusName { get; init; } = string.Empty;
    public int CompletionPercentage { get; init; }
    public int CreatedById { get; init; }
    public string? CreatedByName { get; init; }
    public int ProjectManagerId { get; init; }
    public string? ProjectManagerName { get; init; }
    public int? ScrumMasterId { get; init; }
    public string? ScrumMasterName { get; init; }
    public IReadOnlyList<ProjectMemberSummary> Members { get; init; } = Array.Empty<ProjectMemberSummary>();
    public DateTime StartDate { get; init; }
    public DateTime EndDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
}

public sealed record ProjectMemberSummary
{
    public int AccountId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}
