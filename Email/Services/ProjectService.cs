using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Common;
using TaskManagement.DTOs.Project;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class ProjectService : IProjectService
{
    private const int CompletedStatusId = 4; // matches legacy convention used in TaskItem.StatusId

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IProjectAuthService _projectAuth;

    public ProjectService(AccountDbContext context, IProjectAuthService projectAuth, IEmailService emailService)
    {
        _context = context;
        _projectAuth = projectAuth;
        _emailService = emailService;
    }

    // ----------------------------------------------------------------------
    // Listing
    // ----------------------------------------------------------------------

    public async Task<PagedResult<ProjectListItemResponse>> ListAsync(int requesterId, bool includeDeleted,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var requesterRole = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Role)
            .FirstOrDefaultAsync(cancellationToken);

        if (requesterRole == null)
            throw new ForbiddenException("Requester account not found.");

        var isAdmin = requesterRole == AppRoles.Admin;

        IQueryable<Project> query = _context.Projects.AsNoTracking();

        if (!includeDeleted)
            query = query.Where(p => !p.IsDeleted);
        else if (!isAdmin)
            // Non-admins can only ever see deleted projects they were members of.
            query = query.Where(p => p.IsDeleted);

        if (!isAdmin)
        {
            // Non-admins see only projects where they are a (current or historical) member.
            query = query.Where(p => p.Members.Any(m => m.AccountId == requesterId));
        }

        var total = await query.CountAsync(cancellationToken);

        var pageItems = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProjectListItemResponse
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                StatusId = p.StatusId,
                StatusName = p.Status.Name,
                CreatedById = p.CreatedById,
                CreatedByName = p.CreatedBy.Name,
                ProjectManagerId = p.ProjectManagerId,
                ProjectManagerName = p.Members
                    .Where(m => m.AccountId == p.ProjectManagerId)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                ScrumMasterId = p.ScrumMasterId,
                ScrumMasterName = p.Members
                    .Where(m => m.AccountId == p.ScrumMasterId)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                MemberNames = p.Members
                    .Where(m => m.Role == AppRoles.Member && !m.IsDeleted)
                    .Select(m => m.Account.Name)
                    .ToList(),
                StartDate = p.StartDate,
                EndDate = p.EndDate,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                IsDeleted = p.IsDeleted,
                DeletedAt = p.DeletedAt
            })
            .ToListAsync(cancellationToken);

        // Single batched query to get completion percentages for the page.
        var completion = await GetCompletionMapAsync(pageItems.Select(p => p.Id), cancellationToken);
        var enriched = pageItems
            .Select(p => p with
            {
                CompletionPercentage = completion.TryGetValue(p.Id, out var cp) ? cp : 0
            })
            .ToList();

        return new PagedResult<ProjectListItemResponse>(enriched, total, page, pageSize);
    }

    // ----------------------------------------------------------------------
    // Single project read
    // ----------------------------------------------------------------------

    public async Task<ProjectResponse?> FindAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);
        if (!membership.IsAdmin && !membership.IsMember)
            throw new ForbiddenException("You are not a member of this project.");

        var project = await _context.Projects
            .AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.StatusId,
                StatusName = p.Status.Name,
                p.CreatedById,
                CreatedByName = p.CreatedBy.Name,
                p.ProjectManagerId,
                ProjectManagerName = p.Members
                    .Where(m => m.AccountId == p.ProjectManagerId)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                p.ScrumMasterId,
                ScrumMasterName = p.Members
                    .Where(m => m.AccountId == p.ScrumMasterId)
                    .Select(m => m.Account.Name)
                    .FirstOrDefault(),
                Members = p.Members
                    .Where(m => !m.IsDeleted)
                    .Select(m => new ProjectMemberSummary
                    {
                        AccountId = m.AccountId,
                        Name = m.Account.Name,
                        Role = m.Role
                    })
                    .ToList(),
                p.StartDate,
                p.EndDate,
                p.CreatedAt,
                p.UpdatedAt,
                p.IsDeleted,
                p.DeletedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
            return null;

        var completion = await GetCompletionMapAsync(new[] { project.Id }, cancellationToken);

        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            StatusId = project.StatusId,
            StatusName = project.StatusName,
            CompletionPercentage = completion.TryGetValue(project.Id, out var cp) ? cp : 0,
            CreatedById = project.CreatedById,
            CreatedByName = project.CreatedByName,
            ProjectManagerId = project.ProjectManagerId,
            ProjectManagerName = project.ProjectManagerName,
            ScrumMasterId = project.ScrumMasterId,
            ScrumMasterName = project.ScrumMasterName,
            Members = project.Members,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            IsDeleted = project.IsDeleted,
            DeletedAt = project.DeletedAt
        };
    }

    // ----------------------------------------------------------------------
    // Create
    // ----------------------------------------------------------------------

    public async Task<ProjectResponse> CreateAsync(int requesterId, CreateProjectRequest dto,
        CancellationToken cancellationToken = default)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        var errors = new Dictionary<string, string[]>();

        if (name.Length == 0)
            errors[nameof(dto.Name)] = new[] { "Project name is required." };
        if (dto.StartDate == default)
            errors[nameof(dto.StartDate)] = new[] { "Start date is required." };
        if (dto.EndDate == default)
            errors[nameof(dto.EndDate)] = new[] { "End date is required." };
        if (dto.StartDate != default && dto.EndDate != default && dto.EndDate <= dto.StartDate)
            errors[nameof(dto.EndDate)] = new[] { "End date must be after start date." };

        var memberIds = (dto.MemberIds ?? new List<int>()).Where(x => x > 0).ToList();
        if (memberIds.Count != memberIds.Distinct().Count())
            errors[nameof(dto.MemberIds)] = new[] { "Duplicate member IDs are not allowed." };

        if (errors.Count > 0)
            throw new ValidationException(errors);

        var creator = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name, a.Email, a.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (creator == null)
            throw new ForbiddenException("Creator account not found.");

        var isAdmin = creator.Role == AppRoles.Admin;

        // Determine PM and SM per role rules.
        int projectManagerId;
        int? scrumMasterId;
        if (isAdmin)
        {
            if (dto.ProjectManagerId == null || dto.ProjectManagerId.Value <= 0)
                throw new ValidationException(nameof(dto.ProjectManagerId), "Admin must select a Project Manager.");

            projectManagerId = dto.ProjectManagerId.Value;
            if (dto.IsAlsoScrumMaster)
                scrumMasterId = projectManagerId;
            else
                scrumMasterId = dto.ScrumMasterId is > 0 ? dto.ScrumMasterId : null;
        }
        else
        {
            // Non-admin: creator becomes PM. SM defaults to creator unless they explicitly choose someone else.
            projectManagerId = requesterId;
            if (dto.IsAlsoScrumMaster)
                scrumMasterId = requesterId;
            else if (dto.ScrumMasterId is int sm && sm > 0 && sm != requesterId)
                scrumMasterId = sm;
            else
                scrumMasterId = requesterId;
        }

        // Validate all referenced accounts exist in one round-trip.
        var idsToVerify = new HashSet<int> { projectManagerId };
        if (scrumMasterId.HasValue) idsToVerify.Add(scrumMasterId.Value);
        foreach (var id in memberIds) idsToVerify.Add(id);

        var existingIds = await _context.Accounts
            .AsNoTracking()
            .Where(a => idsToVerify.Contains(a.Id))
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);

        var missing = idsToVerify.Except(existingIds).ToList();
        if (missing.Count > 0)
            throw new ValidationException("memberIds",
                $"The following account ids do not exist: {string.Join(", ", missing)}.");

        // Build the project + memberships in one tracked graph; single SaveChanges.
        var now = PhTime;
        var project = new Project
        {
            Name = name,
            Description = dto.Description,
            CreatedById = requesterId,
            ProjectManagerId = projectManagerId,
            ScrumMasterId = scrumMasterId,
            StatusId = 1, // Not Started — promoted to Active when first task is created
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            CreatedAt = now,
            UpdatedAt = now
        };

        var pmRole = scrumMasterId == projectManagerId ? AppRoles.PmScrumMaster : AppRoles.ProjectManager;
        project.Members.Add(new ProjectMember
        {
            AccountId = projectManagerId,
            Role = pmRole,
            JoinedAt = now
        });

        if (scrumMasterId.HasValue && scrumMasterId.Value != projectManagerId)
        {
            project.Members.Add(new ProjectMember
            {
                AccountId = scrumMasterId.Value,
                Role = AppRoles.ScrumMaster,
                JoinedAt = now
            });
        }

        foreach (var memberId in memberIds.Distinct())
        {
            if (memberId == projectManagerId || memberId == scrumMasterId)
                continue;
            project.Members.Add(new ProjectMember
            {
                AccountId = memberId,
                Role = AppRoles.Member,
                JoinedAt = now
            });
        }

        _context.Projects.Add(project);

        // Notifications written in same SaveChanges to avoid double round-trips.
        QueueAssignmentNotifications(project, projectManagerId, scrumMasterId, memberIds, now);

        _context.AuditLogs.Add(new AuditLog
        {
            Project = project,
            AccountId = requesterId,
            Action = "CREATED",
            NewValue = project.Name,
            Note = $"Created project '{project.Name}' by {creator.Name}.",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        // Emails fire after the row is durable; don't block the API response longer than needed.
        await SendCreationEmailsAsync(project, projectManagerId, scrumMasterId, memberIds, cancellationToken);

        var statusName = await _context.ProjectStatuses
            .AsNoTracking()
            .Where(s => s.Id == project.StatusId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Not Started";

        var memberSummaries = await _context.ProjectMembers
            .AsNoTracking()
            .Where(m => m.ProjectId == project.Id && !m.IsDeleted)
            .Select(m => new ProjectMemberSummary
            {
                AccountId = m.AccountId,
                Name = m.Account.Name,
                Role = m.Role
            })
            .ToListAsync(cancellationToken);

        var pmName = memberSummaries.FirstOrDefault(m => m.AccountId == projectManagerId)?.Name;
        var smName = scrumMasterId.HasValue
            ? memberSummaries.FirstOrDefault(m => m.AccountId == scrumMasterId.Value)?.Name
            : null;

        return new ProjectResponse
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            StatusId = project.StatusId,
            StatusName = statusName,
            CompletionPercentage = 0,
            CreatedById = project.CreatedById,
            CreatedByName = creator.Name,
            ProjectManagerId = project.ProjectManagerId,
            ProjectManagerName = pmName,
            ScrumMasterId = project.ScrumMasterId,
            ScrumMasterName = smName,
            Members = memberSummaries,
            StartDate = project.StartDate,
            EndDate = project.EndDate,
            CreatedAt = project.CreatedAt,
            UpdatedAt = project.UpdatedAt,
            IsDeleted = project.IsDeleted,
            DeletedAt = project.DeletedAt
        };
    }

    // ----------------------------------------------------------------------
    // Update (diff-based)
    // ----------------------------------------------------------------------

    public async Task UpdateAsync(int requesterId, int projectId, UpdateProjectRequest dto,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .FirstOrDefaultAsync(p => p.Id == projectId && !p.IsDeleted, cancellationToken);
        if (project == null)
            throw new NotFoundException("Project not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);
        if (!membership.IsAdmin && !membership.IsProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can update this project.");

        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name, a.Role })
            .FirstAsync(cancellationToken);

        var changes = new List<string>();
        var now = PhTime;

        if (dto.Name != null)
        {
            var trimmed = dto.Name.Trim();
            if (trimmed.Length > 0 && trimmed != project.Name)
            {
                changes.Add($"Name: {project.Name} → {trimmed}");
                project.Name = trimmed;
            }
        }

        if (dto.Description != null && dto.Description != project.Description)
        {
            changes.Add("Description updated");
            project.Description = dto.Description;
        }

        if (dto.StatusId.HasValue && dto.StatusId.Value != project.StatusId)
        {
            var statusOk = await _context.ProjectStatuses
                .AsNoTracking()
                .AnyAsync(s => s.Id == dto.StatusId.Value, cancellationToken);
            if (!statusOk)
                throw new ValidationException(nameof(dto.StatusId), "Invalid StatusId.");
            changes.Add($"StatusId: {project.StatusId} → {dto.StatusId.Value}");
            project.StatusId = dto.StatusId.Value;
        }

        if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate.Value <= dto.StartDate.Value)
            throw new ValidationException(nameof(dto.EndDate), "End date must be after start date.");

        if (dto.StartDate.HasValue && dto.StartDate.Value != project.StartDate)
        {
            changes.Add($"StartDate: {project.StartDate:yyyy-MM-dd} → {dto.StartDate.Value:yyyy-MM-dd}");
            project.StartDate = dto.StartDate.Value;
        }
        if (dto.EndDate.HasValue && dto.EndDate.Value != project.EndDate)
        {
            changes.Add($"EndDate: {project.EndDate:yyyy-MM-dd} → {dto.EndDate.Value:yyyy-MM-dd}");
            project.EndDate = dto.EndDate.Value;
        }

        // Only Admin can rotate the Project Manager.
        if (membership.IsAdmin && dto.ProjectManagerId.HasValue && dto.ProjectManagerId.Value != project.ProjectManagerId)
        {
            await UpsertManagerMembershipAsync(projectId, project.ProjectManagerId, dto.ProjectManagerId.Value,
                AppRoles.ProjectManager, now, cancellationToken);
            changes.Add($"Project Manager: {project.ProjectManagerId} → {dto.ProjectManagerId.Value}");
            project.ProjectManagerId = dto.ProjectManagerId.Value;
        }

        // Scrum Master is tri-state via ScrumMasterIdProvided.
        if (dto.ScrumMasterIdProvided && dto.ScrumMasterId != project.ScrumMasterId)
        {
            await UpsertScrumMasterMembershipAsync(projectId, project.ScrumMasterId, dto.ScrumMasterId,
                project.ProjectManagerId, now, cancellationToken);
            changes.Add($"Scrum Master: {project.ScrumMasterId?.ToString() ?? "—"} → {dto.ScrumMasterId?.ToString() ?? "—"}");
            project.ScrumMasterId = dto.ScrumMasterId;
        }

        // Member-list diff.
        if (dto.MemberIds != null)
        {
            var desired = dto.MemberIds.Where(x => x > 0).ToList();
            if (desired.Count != desired.Distinct().Count())
                throw new ValidationException(nameof(dto.MemberIds), "Duplicate member IDs are not allowed.");

            var currentMembers = await _context.ProjectMembers
                .Where(m => m.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            // Soft-delete members not in the desired set, except PM/SM.
            foreach (var m in currentMembers)
            {
                var isPmOrSm = m.AccountId == project.ProjectManagerId
                            || m.AccountId == project.ScrumMasterId;
                if (m.Role == AppRoles.Member && !isPmOrSm && !desired.Contains(m.AccountId) && !m.IsDeleted)
                {
                    m.IsDeleted = true;
                    m.DeletedAt = now;
                }
            }

            // Add or revive members.
            foreach (var memberId in desired)
            {
                if (memberId == project.ProjectManagerId || memberId == project.ScrumMasterId)
                    continue;

                var existing = currentMembers.FirstOrDefault(m => m.AccountId == memberId);
                if (existing == null)
                {
                    _context.ProjectMembers.Add(new ProjectMember
                    {
                        ProjectId = projectId,
                        AccountId = memberId,
                        Role = AppRoles.Member,
                        JoinedAt = now
                    });
                }
                else if (existing.IsDeleted || existing.Role != AppRoles.Member)
                {
                    existing.IsDeleted = false;
                    existing.DeletedAt = null;
                    existing.Role = AppRoles.Member;
                }
            }

            changes.Add("Members updated");
        }

        if (changes.Count == 0)
            return;

        project.UpdatedAt = now;

        var requesterRoleLabel = membership.IsAdmin ? AppRoles.Admin : (membership.MemberRole ?? "Member");

        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = project.Id,
            AccountId = requesterId,
            Action = "UPDATED",
            NewValue = string.Join(", ", changes),
            Note = $"Project updated '{project.Name}' by {requester.Name} ({requesterRoleLabel}).",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        await SendUpdateEmailsAsync(project, requester.Name, changes, cancellationToken);
    }

    // ----------------------------------------------------------------------
    // Soft delete & restore (cascade via ExecuteUpdateAsync — single SQL UPDATE per table)
    // ----------------------------------------------------------------------

    public async Task SoftDeleteAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Name, p.IsDeleted, p.ProjectManagerId, p.ScrumMasterId })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null || project.IsDeleted)
            throw new NotFoundException("Project not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);
        if (!membership.IsAdmin && !membership.IsProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can delete this project.");

        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Name, a.Role })
            .FirstAsync(cancellationToken);

        var now = PhTime;

        // Cascade soft-delete in batched UPDATEs (no entity tracking).
        await _context.Projects
            .Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsDeleted, true)
                .SetProperty(p => p.DeletedAt, now)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);

        await _context.ProjectMembers
            .Where(m => m.ProjectId == projectId && !m.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDeleted, true)
                .SetProperty(m => m.DeletedAt, (DateTime?)now), cancellationToken);

        await _context.TaskAssignments
            .Where(a => _context.Tasks.Any(t => t.Id == a.TaskId && t.ProjectId == projectId) && !a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, true)
                .SetProperty(a => a.DeletedAt, (DateTime?)now), cancellationToken);

        await _context.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, true)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        var deleterRole = membership.IsAdmin ? AppRoles.Admin : (membership.MemberRole ?? "Project Manager");
        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = projectId,
            AccountId = requesterId,
            Action = "DELETED",
            OldValue = project.Name,
            Note = $"Project '{project.Name}' deleted by {requester.Name} ({deleterRole}).",
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RestoreAsync(int requesterId, int projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await _context.Projects
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Name, p.IsDeleted })
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null || !project.IsDeleted)
            throw new NotFoundException("Project not found.");

        var membership = await _projectAuth.GetMembershipAsync(requesterId, projectId, cancellationToken);
        if (!membership.IsAdmin && !membership.IsProjectManager)
            throw new ForbiddenException("Only the Project Manager or Admin can restore this project.");

        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Name })
            .FirstAsync(cancellationToken);

        var now = PhTime;

        await _context.Projects
            .Where(p => p.Id == projectId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.IsDeleted, false)
                .SetProperty(p => p.DeletedAt, (DateTime?)null)
                .SetProperty(p => p.UpdatedAt, now), cancellationToken);

        await _context.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.IsDeleted, false)
                .SetProperty(m => m.DeletedAt, (DateTime?)null), cancellationToken);

        var restoredTasks = await _context.Tasks
            .Where(t => t.ProjectId == projectId && t.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsDeleted, false)
                .SetProperty(t => t.UpdatedAt, now), cancellationToken);

        await _context.TaskAssignments
            .Where(a => _context.Tasks.Any(t => t.Id == a.TaskId && t.ProjectId == projectId) && a.IsDeleted)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.IsDeleted, false)
                .SetProperty(a => a.DeletedAt, (DateTime?)null), cancellationToken);

        var requesterRoleLabel = membership.IsAdmin ? AppRoles.Admin : (membership.MemberRole ?? "Project Manager");
        _context.AuditLogs.Add(new AuditLog
        {
            ProjectId = projectId,
            AccountId = requesterId,
            Action = "RESTORED",
            NewValue = project.Name,
            Note = $"Project and all tasks reactivated by {requester.Name} ({requesterRoleLabel}).",
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);

        return restoredTasks;
    }

    // ----------------------------------------------------------------------
    // Status catalog
    // ----------------------------------------------------------------------

    public async Task<IReadOnlyList<ProjectStatusItem>> GetStatusesCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ProjectStatuses
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.Id)
            .Select(s => new ProjectStatusItem
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Active = s.IsActive,
                CreatedAt = s.CreatedAt
            })
            .ToListAsync(cancellationToken);
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    private async Task<Dictionary<int, int>> GetCompletionMapAsync(IEnumerable<int> projectIds,
        CancellationToken cancellationToken)
    {
        var ids = projectIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, int>();

        var rows = await _context.Tasks
            .AsNoTracking()
            .Where(t => ids.Contains(t.ProjectId) && !t.IsDeleted && t.ParentTaskId == null)
            .GroupBy(t => t.ProjectId)
            .Select(g => new
            {
                ProjectId = g.Key,
                Total = g.Count(),
                Completed = g.Count(x => x.StatusId == CompletedStatusId)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.ProjectId,
            x => x.Total == 0 ? 0 : (int)Math.Round((double)x.Completed / x.Total * 100));
    }

    private async Task UpsertManagerMembershipAsync(int projectId, int oldPmId, int newPmId, string role,
        DateTime now, CancellationToken cancellationToken)
    {
        // Soft-delete the previous PM membership.
        var oldPm = await _context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == oldPmId && !m.IsDeleted,
                cancellationToken);
        if (oldPm != null)
        {
            oldPm.IsDeleted = true;
            oldPm.DeletedAt = now;
        }

        // Promote or insert the new PM.
        var newPm = await _context.ProjectMembers
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == newPmId,
                cancellationToken);
        if (newPm == null)
        {
            _context.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                AccountId = newPmId,
                Role = role,
                JoinedAt = now
            });
        }
        else
        {
            newPm.IsDeleted = false;
            newPm.DeletedAt = null;
            // If previously a Scrum Master, promote to combined PM/SM role.
            newPm.Role = newPm.Role == AppRoles.ScrumMaster ? AppRoles.PmScrumMaster : role;
        }
    }

    private async Task UpsertScrumMasterMembershipAsync(int projectId, int? oldSmId, int? newSmId,
        int pmId, DateTime now, CancellationToken cancellationToken)
    {
        if (oldSmId.HasValue)
        {
            var oldSm = await _context.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == oldSmId.Value && !m.IsDeleted,
                    cancellationToken);
            if (oldSm != null)
            {
                if (oldSm.Role == AppRoles.ScrumMaster)
                {
                    oldSm.IsDeleted = true;
                    oldSm.DeletedAt = now;
                }
                else if (oldSm.Role == AppRoles.PmScrumMaster)
                {
                    // Demote combined role back to plain PM.
                    oldSm.Role = AppRoles.ProjectManager;
                }
            }
        }

        if (newSmId.HasValue)
        {
            var newSm = await _context.ProjectMembers
                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == newSmId.Value,
                    cancellationToken);
            if (newSm == null)
            {
                _context.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = projectId,
                    AccountId = newSmId.Value,
                    Role = newSmId.Value == pmId ? AppRoles.PmScrumMaster : AppRoles.ScrumMaster,
                    JoinedAt = now
                });
            }
            else
            {
                newSm.IsDeleted = false;
                newSm.DeletedAt = null;
                newSm.Role = newSm.Role == AppRoles.ProjectManager
                    ? AppRoles.PmScrumMaster
                    : (newSmId.Value == pmId ? AppRoles.PmScrumMaster : AppRoles.ScrumMaster);
            }
        }
    }

    private void QueueAssignmentNotifications(Project project, int pmId, int? smId, IEnumerable<int> memberIds,
        DateTime now)
    {
        if (smId == pmId || !smId.HasValue)
        {
            _context.Notifications.Add(NewNotification(pmId, project,
                $"You have been assigned as Project Manager - Scrum Master of '{project.Name}'.", now));
        }
        else
        {
            _context.Notifications.Add(NewNotification(pmId, project,
                $"You have been assigned as Project Manager of '{project.Name}'.", now));
            _context.Notifications.Add(NewNotification(smId.Value, project,
                $"You have been assigned as Scrum Master of '{project.Name}'.", now));
        }

        foreach (var memberId in memberIds.Distinct())
        {
            if (memberId == pmId || memberId == smId)
                continue;
            _context.Notifications.Add(NewNotification(memberId, project,
                $"You have been added as a Member to project '{project.Name}'.", now));
        }
    }

    private static Notification NewNotification(int accountId, Project project, string message, DateTime now)
        => new()
        {
            AccountId = accountId,
            Project = project,
            Message = message,
            IsRead = false,
            CreatedAt = now
        };

    private async Task SendCreationEmailsAsync(Project project, int pmId, int? smId, IEnumerable<int> memberIds,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<int> { pmId };
        if (smId.HasValue) ids.Add(smId.Value);
        foreach (var id in memberIds) ids.Add(id);

        var emails = await _context.Accounts
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.Name, a.Email })
            .ToListAsync(cancellationToken);
        var byId = emails.ToDictionary(x => x.Id);

        async Task SendAsync(int id, string subject, string body)
        {
            if (!byId.TryGetValue(id, out var acc) || string.IsNullOrEmpty(acc.Email))
                return;
            await _emailService.SendEmailAsync(acc.Email, subject, body);
        }

        var pmRoleLabel = smId == pmId ? "Project Manager & Scrum Master" : "Project Manager";
        if (byId.TryGetValue(pmId, out var pm))
        {
            await SendAsync(pmId,
                $"You've been assigned to project: {project.Name}",
                $@"<h2>Project Assignment</h2>
                   <p>Hello <strong>{pm.Name}</strong>,</p>
                   <p>You have been assigned as <strong>{pmRoleLabel}</strong> of project <strong>{project.Name}</strong>.</p>
                   <p><strong>Start Date:</strong> {project.StartDate:MMMM dd, yyyy}</p>
                   <p><strong>End Date:</strong> {project.EndDate:MMMM dd, yyyy}</p>
                   <p>Please log in to view the project details.</p>");
        }

        if (smId.HasValue && smId.Value != pmId && byId.TryGetValue(smId.Value, out var sm))
        {
            await SendAsync(smId.Value,
                $"You've been assigned to project: {project.Name}",
                $@"<h2>Project Assignment</h2>
                   <p>Hello <strong>{sm.Name}</strong>,</p>
                   <p>You have been assigned as <strong>Scrum Master</strong> of project <strong>{project.Name}</strong>.</p>
                   <p><strong>Start Date:</strong> {project.StartDate:MMMM dd, yyyy}</p>
                   <p><strong>End Date:</strong> {project.EndDate:MMMM dd, yyyy}</p>
                   <p>Please log in to view the project details.</p>");
        }

        foreach (var memberId in memberIds.Distinct())
        {
            if (memberId == pmId || memberId == smId) continue;
            if (!byId.TryGetValue(memberId, out var member)) continue;
            await SendAsync(memberId,
                $"You've been added to project: {project.Name}",
                $@"<h2>Project Member Assignment</h2>
                   <p>Hello <strong>{member.Name}</strong>,</p>
                   <p>You have been added as a <strong>Member</strong> of project <strong>{project.Name}</strong>.</p>
                   <p><strong>Start Date:</strong> {project.StartDate:MMMM dd, yyyy}</p>
                   <p><strong>End Date:</strong> {project.EndDate:MMMM dd, yyyy}</p>
                   <p>Please log in to view the project details.</p>");
        }
    }

    private async Task SendUpdateEmailsAsync(Project project, string requesterName, IReadOnlyList<string> changes,
        CancellationToken cancellationToken)
    {
        var recipientIds = new HashSet<int> { project.ProjectManagerId };
        if (project.ScrumMasterId.HasValue && project.ScrumMasterId.Value != project.ProjectManagerId)
            recipientIds.Add(project.ScrumMasterId.Value);

        var recipients = await _context.Accounts
            .AsNoTracking()
            .Where(a => recipientIds.Contains(a.Id))
            .Select(a => new { a.Name, a.Email })
            .ToListAsync(cancellationToken);

        var changeList = string.Join("", changes.Select(c => $"<li>{c}</li>"));
        foreach (var r in recipients)
        {
            if (string.IsNullOrEmpty(r.Email)) continue;
            await _emailService.SendEmailAsync(
                r.Email,
                $"Project Updated: {project.Name}",
                $@"<h2>Project Update Notification</h2>
                   <p>Hello <strong>{r.Name}</strong>,</p>
                   <p>The project <strong>{project.Name}</strong> has been updated by <strong>{requesterName}</strong>.</p>
                   <p><strong>Changes:</strong></p>
                   <ul>{changeList}</ul>
                   <p>Please log in to review the changes.</p>");
        }
    }
}
