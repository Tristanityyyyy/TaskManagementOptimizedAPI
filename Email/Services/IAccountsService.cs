using TaskManagement.DTOs.Account;
using TaskManagement.DTOs.Common;

namespace TaskManagement.Services;

public interface IAccountsService
{
    Task<PagedResult<AccountListItemResponse>> ListAsync(int requesterId, string? role, bool? active,
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<AccountResponse?> FindAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default);

    Task<AccountResponse> GetCurrentAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AccountStatsResponse>> GetUsersWithStatsAsync(int requesterId,
        CancellationToken cancellationToken = default);

    Task<AccountResponse> CreateAsync(int requesterId, CreateAccountRequest dto,
        CancellationToken cancellationToken = default);

    Task<UpdateAccountResponse> UpdateAsync(int requesterId, int accountId, UpdateAccountRequest dto,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default);

    Task ReactivateAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default);

    Task<ProfilePictureResponse> RemoveProfilePictureAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default);

    Task<ProfilePictureResponse> UploadProfilePictureAsync(int requesterId, int accountId,
        Stream fileStream, string fileName, long fileLength,
        CancellationToken cancellationToken = default);

    GeneratedPasswordResponse GeneratePassword();
}
