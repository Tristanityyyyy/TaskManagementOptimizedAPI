namespace TaskManagement.DTOs.Account;

public sealed record AccountStatsResponse
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string? Specialization { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int ProjectCount { get; init; }
    public int ActiveTaskCount { get; init; }
}
