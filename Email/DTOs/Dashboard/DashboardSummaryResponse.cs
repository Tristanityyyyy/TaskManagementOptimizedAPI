namespace TaskManagement.DTOs.Dashboard;

/// <summary>
/// Combined summary card payload. Replaces the legacy split between
/// <c>GetDashboardAdminStats</c> / <c>GetDashboardUserStats</c> + the per-user counts the
/// frontend used to derive by walking the full project+task tree client-side.
/// </summary>
public sealed record DashboardSummaryResponse
{
    public string Role { get; init; } = string.Empty;

    /// <summary>
    /// Counts scoped to the requester (admins still see global, members see only their projects/tasks).
    /// </summary>
    public DashboardUserCounts MyCounts { get; init; } = default!;

    /// <summary>
    /// Admin-only system-wide counts. Null for non-admin requesters.
    /// </summary>
    public DashboardAdminCounts? AdminCounts { get; init; }
}

public sealed record DashboardUserCounts
{
    public int Projects { get; init; }
    public int Tasks { get; init; }
    public int ForReview { get; init; }
    public int Completed { get; init; }
}

public sealed record DashboardAdminCounts
{
    public int TotalUsers { get; init; }
    public int TotalProjects { get; init; }
    public int OverdueTasks { get; init; }
    public int DeactivatedUsers { get; init; }
}
