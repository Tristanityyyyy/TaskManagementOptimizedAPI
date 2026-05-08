using TaskManagement.DTOs.Dashboard;

namespace TaskManagement.Services;

public interface IDashboardService
{
    Task<DashboardSummaryResponse> GetSummaryAsync(int requesterId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DashboardProjectResponse>> GetMyProjectsAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CalendarTaskResponse>> GetCalendarTasksAsync(int requesterId, DateTime? from, DateTime? to,
        CancellationToken cancellationToken = default);

    Task<ProjectTaskSummaryResponse> GetProjectTaskSummaryAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default);
}
