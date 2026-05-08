namespace TaskManagement.Authorization;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string ProjectManager = "Project Manager";
    public const string ScrumMaster = "Scrum Master";
    public const string PmScrumMaster = "Project Manager - Scrum Master";
    public const string Member = "Member";

    public static readonly string[] CanManageTasks =
    {
        Admin,
        ProjectManager,
        ScrumMaster,
        PmScrumMaster
    };
}
