namespace TaskManagement.DTOs.Project
{
    public class ProjectResponseDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int StatusId { get; set; }
        public string StatusName { get; set; } = string.Empty;
        public int CompletionPercentage { get; set; }
        public int CreatedById { get; set; }
        public string? CreatedByName { get; set; }
        public int ProjectManagerId { get; set; }
        public string? ProjectManagerName { get; set; }
        public string? ScrumMasterName { get; set; }
        public int? ScrumMasterId { get; set; }
        public List<string> MemberNames { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}