using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Comment;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/tasks/{taskId:int}/comments")]
public sealed class TaskCommentsController : ApiControllerBase
{
    private readonly ICommentService _comments;

    public TaskCommentsController(ICommentService comments)
    {
        _comments = comments;
    }

    [HttpGet(Name = nameof(ListByTask))]
    [ProducesResponseType(typeof(IReadOnlyList<CommentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CommentResponse>>> ListByTask(int taskId,
        CancellationToken cancellationToken = default)
        => Ok(await _comments.ListByTaskAsync(CurrentAccount.Id, taskId, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(CommentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommentResponse>> Create(int taskId, [FromBody] CreateCommentRequest dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _comments.CreateAsync(CurrentAccount.Id, taskId, dto, cancellationToken);
        return CreatedAtAction(nameof(ListByTask), new { taskId }, created);
    }

    [HttpPatch("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int taskId, int id, [FromBody] UpdateCommentRequest dto,
        CancellationToken cancellationToken = default)
    {
        await _comments.UpdateAsync(CurrentAccount.Id, id, dto, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int taskId, int id,
        CancellationToken cancellationToken = default)
    {
        await _comments.DeleteAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }
}
