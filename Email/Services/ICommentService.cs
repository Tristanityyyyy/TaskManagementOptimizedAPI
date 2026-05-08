using TaskManagement.DTOs.Comment;

namespace TaskManagement.Services;

public interface ICommentService
{
    Task<IReadOnlyList<CommentResponse>> ListByTaskAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default);

    Task<CommentResponse> CreateAsync(int requesterId, int taskId, CreateCommentRequest dto,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(int requesterId, int commentId, UpdateCommentRequest dto,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(int requesterId, int commentId,
        CancellationToken cancellationToken = default);
}
