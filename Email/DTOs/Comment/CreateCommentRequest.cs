using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Comment;

public sealed record CreateCommentRequest
{
    [Required]
    [MinLength(1)]
    public string Content { get; init; } = string.Empty;
}
