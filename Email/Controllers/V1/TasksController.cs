using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Task;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/tasks")]
public sealed class TasksController : ApiControllerBase
{
    private readonly ITaskService _tasks;

    public TasksController(ITaskService tasks)
    {
        _tasks = tasks;
    }

    [HttpGet(Name = nameof(List))]
    [ProducesResponseType(typeof(PagedResult<TaskListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<TaskListItemResponse>>> List(
        [FromQuery] int projectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _tasks.ListByProjectAsync(CurrentAccount.Id, projectId, page, pageSize, cancellationToken));

    [HttpGet("assigned-to-me")]
    [ProducesResponseType(typeof(PagedResult<TaskListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<TaskListItemResponse>>> AssignedToMe(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _tasks.ListAssignedToMeAsync(CurrentAccount.Id, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TaskListItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TaskListItemResponse>> Get(int id,
        CancellationToken cancellationToken = default)
    {
        var task = await _tasks.FindAsync(CurrentAccount.Id, id, cancellationToken);
        if (task == null)
            return NotFound();
        return Ok(task);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TaskResponse>> Create(
        [FromBody] CreateTaskRequest dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _tasks.CreateAsync(CurrentAccount.Id, dto, cancellationToken);
        return CreatedAtAction(nameof(List), new { projectId = created.ProjectId, page = 1, pageSize = 20 },
            created);
    }

    [HttpPatch("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTaskRequest dto,
        CancellationToken cancellationToken = default)
    {
        await _tasks.UpdateAsync(CurrentAccount.Id, id, dto, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateTaskStatusRequest dto,
        CancellationToken cancellationToken = default)
    {
        await _tasks.UpdateStatusAsync(CurrentAccount.Id, id, dto, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:int}/assignees")]
    [ProducesResponseType(typeof(AssignTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignTaskResponse>> Assign(int id, [FromBody] AssignTaskRequest dto,
        CancellationToken cancellationToken = default)
        => Ok(await _tasks.AssignAsync(CurrentAccount.Id, id, dto, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id,
        CancellationToken cancellationToken = default)
    {
        await _tasks.SoftDeleteAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:int}/restore")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(int id,
        CancellationToken cancellationToken = default)
    {
        var restoredCount = await _tasks.RestoreAsync(CurrentAccount.Id, id, cancellationToken);
        return Ok(new { restoredCount });
    }
}
