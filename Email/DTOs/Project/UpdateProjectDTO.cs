namespace TaskManagement.DTOs.Project
{
    public class UpdateProjectDTO
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public int? StatusId { get; set; } // 1 Not Started, 2 Active, 3 Completed
        public int? ProjectManagerId { get; set; }
        public int? ScrumMasterId { get; set; }
        public List<int>? AssigneeIds { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
