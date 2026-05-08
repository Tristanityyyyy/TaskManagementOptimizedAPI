using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Project;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/projects")]
public sealed class ProjectsController : ApiControllerBase
{
    private readonly IProjectService _projects;

    public ProjectsController(IProjectService projects)
    {
        _projects = projects;
    }

    [HttpGet("catalog/statuses")]
    [ProducesResponseType(typeof(IReadOnlyList<ProjectStatusItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<ProjectStatusItem>>> CatalogStatuses(
        CancellationToken cancellationToken = default)
        => Ok(await _projects.GetStatusesCatalogAsync(cancellationToken));

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProjectListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ProjectListItemResponse>>> List(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
        => Ok(await _projects.ListAsync(CurrentAccount.Id, includeDeleted, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectResponse>> Get(int id,
        CancellationToken cancellationToken = default)
    {
        var project = await _projects.FindAsync(CurrentAccount.Id, id, cancellationToken);
        if (project == null)
            return NotFound();
        return Ok(project);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ProjectResponse>> Create(
        [FromBody] CreateProjectRequest dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _projects.CreateAsync(CurrentAccount.Id, dto, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateProjectRequest dto,
        CancellationToken cancellationToken = default)
    {
        await _projects.UpdateAsync(CurrentAccount.Id, id, dto, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id,
        CancellationToken cancellationToken = default)
    {
        await _projects.SoftDeleteAsync(CurrentAccount.Id, id, cancellationToken);
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
        var restoredCount = await _projects.RestoreAsync(CurrentAccount.Id, id, cancellationToken);
        return Ok(new { restoredCount });
    }
}
