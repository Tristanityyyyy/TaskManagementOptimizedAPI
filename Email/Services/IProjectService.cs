using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Project;

namespace TaskManagement.Services;

public interface IProjectService
{
    Task<PagedResult<ProjectListItemResponse>> ListAsync(int requesterId, int page, int pageSize,
        bool includeDeleted, CancellationToken cancellationToken = default);

    Task<ProjectListItemResponse?> FindAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default);

    Task<ProjectListItemResponse> CreateAsync(int creatorId, CreateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task<ProjectListItemResponse> UpdateAsync(int requesterId, int projectId, UpdateProjectRequest request,
        CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(int requesterId, int projectId, CancellationToken cancellationToken = default);

    Task RestoreAsync(int requesterId, int projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectStatusItem>> GetStatusesCatalogAsync(CancellationToken cancellationToken = default);
}
