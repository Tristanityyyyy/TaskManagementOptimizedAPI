using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Dashboard;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController : ApiControllerBase
{
    private readonly IDashboardService _dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        _dashboard = dashboard;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(DashboardSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardSummaryResponse>> Summary(
        CancellationToken cancellationToken = default)
        => Ok(await _dashboard.GetSummaryAsync(CurrentAccount.Id, cancellationToken));

    [HttpGet("projects")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardProjectResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<DashboardProjectResponse>>> Projects(
        CancellationToken cancellationToken = default)
        => Ok(await _dashboard.GetMyProjectsAsync(CurrentAccount.Id, cancellationToken));

    [HttpGet("calendar")]
    [ProducesResponseType(typeof(IReadOnlyList<CalendarTaskResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<CalendarTaskResponse>>> Calendar(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken cancellationToken = default)
        => Ok(await _dashboard.GetCalendarTasksAsync(CurrentAccount.Id, from, to, cancellationToken));

    [HttpGet("projects/{projectId:int}/task-summary")]
    [ProducesResponseType(typeof(ProjectTaskSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProjectTaskSummaryResponse>> ProjectTaskSummary(int projectId,
        CancellationToken cancellationToken = default)
        => Ok(await _dashboard.GetProjectTaskSummaryAsync(CurrentAccount.Id, projectId, cancellationToken));
}
