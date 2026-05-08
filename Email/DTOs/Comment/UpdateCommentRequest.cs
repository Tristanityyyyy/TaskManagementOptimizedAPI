using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Comment;

public sealed record UpdateCommentRequest
{
    [Required]
    [MinLength(1)]
    public string Content { get; init; } = string.Empty;
}
