namespace TaskManagement.Auth;

public sealed record ResolvedAccount
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string Role { get; init; }
}
