namespace TaskManagement.DTOs.Project
{
    public class CreateProjectDTO
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int? ProjectManagerId { get; set; }  // required if Admin, ignored if User
        public int? ScrumMasterId { get; set; }     // optional for both
        public List<int> MemberIds { get; set; } = new List<int>();
        public bool IsAlsoScrumMaster { get; set; } = false; // for User: true if they want to be SM too

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }
}
