using TaskManagement.DTOs.Auth;

namespace TaskManagement.Services;

public interface IAuthService
{
    Task<LoginResponseV1> LoginAsync(LoginRequestV1 dto, CancellationToken cancellationToken = default);

    Task<AuthUserSummary> GetMeAsync(int requesterId, CancellationToken cancellationToken = default);

    Task LogoutAsync(int requesterId, CancellationToken cancellationToken = default);

    Task SendForgotPasswordOtpAsync(ForgotPasswordRequestV1 dto, CancellationToken cancellationToken = default);

    Task VerifyOtpAsync(VerifyOtpRequestV1 dto, CancellationToken cancellationToken = default);

    Task ResetPasswordAsync(ResetPasswordRequestV1 dto, CancellationToken cancellationToken = default);
}
