using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Project;

public sealed record CreateProjectRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "Project name is required.")]
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Required when the requester is an Admin; ignored otherwise (creator becomes PM).</summary>
    public int? ProjectManagerId { get; init; }

    /// <summary>Optional. If <see cref="IsAlsoScrumMaster"/> is true, this is ignored.</summary>
    public int? ScrumMasterId { get; init; }

    /// <summary>If true, the Project Manager is also assigned the Scrum Master role.</summary>
    public bool IsAlsoScrumMaster { get; init; }

    public List<int> MemberIds { get; init; } = new();

    [Required]
    public DateTime StartDate { get; init; }

    [Required]
    public DateTime EndDate { get; init; }
}
