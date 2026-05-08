namespace TaskManagement.DTOs.Project;

public sealed record ProjectStatusItem
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool Active { get; init; }
    public DateTime CreatedAt { get; init; }
}
