namespace TaskManagement.DTOs.Auth;

public sealed record LoginResponseV1
{
    public string Token { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public DateTime ExpiresAt { get; init; }
    public AuthUserSummary User { get; init; } = default!;
}

public sealed record AuthUserSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? ProfilePicture { get; init; }
}
