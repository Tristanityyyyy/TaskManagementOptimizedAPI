namespace TaskManagement.Services;

public enum TaskVisibility
{
    None,
    OnlyAssigned,
    Full
}

public sealed record ProjectMembership(
    int AccountId,
    int ProjectId,
    bool IsAdmin,
    bool IsProjectManager,
    bool IsScrumMaster,
    bool IsMember,
    string? MemberRole)
{
    public bool IsPrivileged => IsAdmin || IsProjectManager || IsScrumMaster;
}

public interface IProjectAuthService
{
    Task<bool> CanManageTasksAsync(int accountId, int projectId, CancellationToken cancellationToken = default);

    Task<TaskVisibility> GetTaskVisibilityAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectMembership> GetMembershipAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default);
}
