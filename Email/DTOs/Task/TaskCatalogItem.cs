namespace TaskManagement.DTOs.Task;

public sealed record TaskCatalogItem(int Id, string Name, string? Description, bool Active, DateTime CreatedAt);
