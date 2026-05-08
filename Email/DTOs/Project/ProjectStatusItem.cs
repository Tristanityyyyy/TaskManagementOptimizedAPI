namespace TaskManagement.DTOs.Project;

public sealed record ProjectStatusItem(int Id, string Name, string? Description, bool Active, DateTime CreatedAt);
