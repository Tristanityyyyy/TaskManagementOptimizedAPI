using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;

namespace TaskManagement.Services;

public sealed class ProjectAuthService : IProjectAuthService
{
    private readonly AccountDbContext _context;

    public ProjectAuthService(AccountDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CanManageTasksAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var role = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (role == AppRoles.Admin)
            return true;

        var memberRole = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.AccountId == accountId && !m.IsDeleted)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return memberRole != null && AppRoles.CanManageTasks.Contains(memberRole);
    }

    public async Task<TaskVisibility> GetTaskVisibilityAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);

        if (account == null)
            return TaskVisibility.None;

        if (account.Role == AppRoles.Admin)
            return TaskVisibility.Full;

        var member = await _context.ProjectMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == accountId,
                cancellationToken);

        if (member == null)
            return TaskVisibility.None;

        if (AppRoles.CanManageTasks.Contains(member.Role))
            return TaskVisibility.Full;

        return TaskVisibility.OnlyAssigned;
    }

    public async Task<ProjectMembership> GetMembershipAsync(int accountId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Id, a.Role })
            .FirstOrDefaultAsync(cancellationToken);

        var isAdmin = account != null && account.Role == AppRoles.Admin;

        var memberRole = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.AccountId == accountId && !m.IsDeleted)
            .Select(m => m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        var isPm = memberRole == AppRoles.ProjectManager || memberRole == AppRoles.PmScrumMaster;
        var isSm = memberRole == AppRoles.ScrumMaster || memberRole == AppRoles.PmScrumMaster;
        var isMember = memberRole != null;

        return new ProjectMembership(
            accountId,
            projectId,
            isAdmin,
            isPm,
            isSm,
            isMember,
            memberRole);
    }
}
