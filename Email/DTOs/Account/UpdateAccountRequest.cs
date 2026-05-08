namespace TaskManagement.DTOs.Account;

public sealed record UpdateAccountRequest
{
    public string? Name { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? ProfilePicture { get; init; }
    public string? Specialization { get; init; }

    /// <summary>
    /// Password change is a tri-field operation. Setting any of these requires all three;
    /// the service verifies <see cref="CurrentPassword"/> before applying the change.
    /// </summary>
    public string? CurrentPassword { get; init; }
    public string? NewPassword { get; init; }
    public string? ConfirmPassword { get; init; }
}

public sealed record UpdateAccountResponse
{
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> UpdatedFields { get; init; } = Array.Empty<string>();
    public AccountResponse? Account { get; init; }
}
