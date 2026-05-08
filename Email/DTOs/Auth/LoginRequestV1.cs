using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Auth;

public sealed record LoginRequestV1
{
    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    public bool RememberMe { get; init; }
}
