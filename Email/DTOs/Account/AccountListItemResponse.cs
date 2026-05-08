namespace TaskManagement.DTOs.Account;

public sealed record AccountListItemResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? Specialization { get; init; }
    public string? ProfilePicture { get; init; }
    public bool IsActive { get; init; }
}
