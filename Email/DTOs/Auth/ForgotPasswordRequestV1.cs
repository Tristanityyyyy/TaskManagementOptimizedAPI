using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Auth;

public sealed record ForgotPasswordRequestV1
{
    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; init; } = string.Empty;
}

public sealed record VerifyOtpRequestV1
{
    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; init; } = string.Empty;
}

public sealed record ResetPasswordRequestV1
{
    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string NewPassword { get; init; } = string.Empty;
}
