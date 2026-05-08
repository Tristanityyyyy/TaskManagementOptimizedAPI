using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Task;

namespace TaskManagement.Services;

public interface ITaskService
{
    Task<PagedResult<TaskListItemResponse>> ListByProjectAsync(int requesterId, int projectId, int page,
        int pageSize, CancellationToken cancellationToken = default);

    Task<PagedResult<TaskListItemResponse>> ListAssignedToMeAsync(int requesterId, int page, int pageSize,
        CancellationToken cancellationToken = default);

    Task<TaskListItemResponse?> FindAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default);

    Task<TaskResponse> CreateAsync(int creatorId, CreateTaskRequest dto,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(int updaterId, int taskId, UpdateTaskRequest dto,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(int requesterId, int taskId, UpdateTaskStatusRequest dto,
        CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default);

    Task<int> RestoreAsync(int requesterId, int taskId,
        CancellationToken cancellationToken = default);

    Task<AssignTaskResponse> AssignAsync(int requesterId, int taskId, AssignTaskRequest dto,
        CancellationToken cancellationToken = default);
}
