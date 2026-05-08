using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.StickyNote;

public sealed record StickyNoteResponse
{
    public int Id { get; init; }
    public int AccountId { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool IsPinned { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed record CreateStickyNoteRequest
{
    [Required]
    [MaxLength(500)]
    public string Content { get; init; } = string.Empty;

    public bool IsPinned { get; init; }
}

public sealed record UpdateStickyNoteRequest
{
    [MaxLength(500)]
    public string? Content { get; init; }

    public bool? IsPinned { get; init; }
}
