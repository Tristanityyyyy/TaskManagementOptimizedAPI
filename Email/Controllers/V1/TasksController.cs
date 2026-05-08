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
}
