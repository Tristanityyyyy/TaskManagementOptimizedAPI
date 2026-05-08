using System.ComponentModel.DataAnnotations;

namespace TaskManagement.DTOs.Account;

public sealed record CreateAccountRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(100)]
    public string Email { get; init; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = string.Empty;

    [MaxLength(50)]
    public string? Specialization { get; init; }

    [Required]
    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;
}
