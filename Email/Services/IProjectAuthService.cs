namespace TaskManagement.Services;

public enum TaskVisibility
{
    None,
    OnlyAssigned,
    Full
}

public interface IProjectAuthService
{
    Task<bool> CanManageTasksAsync(int accountId, int projectId, CancellationToken cancellationToken = default);

    Task<TaskVisibility> GetTaskVisibilityAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default);
}
