namespace TaskManagement.DTOs.Comment;

public sealed record CommentResponse
{
    public int Id { get; init; }
    public int TaskId { get; init; }
    public int AccountId { get; init; }
    public string AccountName { get; init; } = "User";
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
