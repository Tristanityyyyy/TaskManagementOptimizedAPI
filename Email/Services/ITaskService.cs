using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Task;

namespace TaskManagement.Services;

public interface ITaskService
{
    Task<PagedResult<TaskListItemResponse>> ListByProjectAsync(int requesterId, int projectId, int page,
        int pageSize, CancellationToken cancellationToken = default);

    Task<TaskResponse> CreateAsync(int creatorId, CreateTaskRequest dto,
        CancellationToken cancellationToken = default);
}
