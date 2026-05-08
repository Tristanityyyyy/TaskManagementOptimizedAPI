using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Project;

namespace TaskManagement.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectListItemResponse>> ListAsync(int requesterId, bool includeDeleted, int page,
        int pageSize, CancellationToken cancellationToken = default);

    Task<ProjectResponse?> FindAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectResponse> CreateAsync(int requesterId, CreateProjectRequest dto,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(int requesterId, int projectId, UpdateProjectRequest dto,
        CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default);

    Task<int> RestoreAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStatusItem>> GetStatusesCatalogAsync(
        CancellationToken cancellationToken = default);
}
