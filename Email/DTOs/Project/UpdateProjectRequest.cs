namespace TaskManagement.DTOs.Project;

public sealed record UpdateProjectRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public int? StatusId { get; init; }
    public int? ProjectManagerId { get; init; }

    /// <summary>
    /// Tri-state behavior:
    ///   - field omitted → no change
    ///   - explicit null  → clear scrum master
    ///   - integer        → set to that account
    /// JSON serializers map missing-vs-null via this nullable. Service treats sentinel via
    /// the <see cref="ScrumMasterIdProvided"/> companion flag below.
    /// </summary>
    public int? ScrumMasterId { get; init; }

    /// <summary>Set true on the Laravel side when the client explicitly sent a value (including null).</summary>
    public bool ScrumMasterIdProvided { get; init; }

    public List<int>? MemberIds { get; init; }

    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}
