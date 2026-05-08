using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Project;

public sealed record CreateProjectRequest
{
    [Required]
    [MinLength(1)]
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Required when the creator is an Admin; ignored for non-admins (creator becomes PM).</summary>
    public int? ProjectManagerId { get; init; }

    public int? ScrumMasterId { get; init; }

    public bool IsAlsoScrumMaster { get; init; }

    public List<int> MemberIds { get; init; } = new();

    [Required]
    public DateTime StartDate { get; init; }

    [Required]
    public DateTime EndDate { get; init; }
}
