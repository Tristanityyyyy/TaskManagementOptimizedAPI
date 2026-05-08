using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using System.Security.Claims;
using System.Threading.Tasks;
using TaskManagement.Data;
using TaskManagement.DTOs.Project;
using TaskManagement.Models;
using TaskManagement.Services;

namespace TaskManagement.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProjectController : ControllerBase
    {
        private readonly AccountDbContext _context;
        private readonly NotificationService _notificationService;
        private readonly IEmailService _emailService;

        private static DateTime PhTime =>
             TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));
        public ProjectController(AccountDbContext context, NotificationService notificationService, IEmailService emailService)
        {
            _context = context;
            _notificationService = notificationService;
            _emailService = emailService;
        }

        private async Task<int> GetProjectCompletionPercentage(int projectId)
        {
            var counts = await _context.Tasks
                .AsNoTracking()
                .Where(t => t.ProjectId == projectId && !t.IsDeleted && t.ParentTaskId == null)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Completed = g.Count(x => x.StatusId == 4)
                })
                .FirstOrDefaultAsync();

            if (counts == null || counts.Total == 0) return 0;
            return (int)Math.Round((double)counts.Completed / counts.Total * 100);
        }

        private async Task<Dictionary<int, int>> GetProjectCompletionPercentageMap(IEnumerable<int> projectIds)
        {
            var ids = projectIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            var rows = await _context.Tasks
                .AsNoTracking()
                .Where(t => ids.Contains(t.ProjectId) && !t.IsDeleted && t.ParentTaskId == null)
                .GroupBy(t => t.ProjectId)
                .Select(g => new
                {
                    ProjectId = g.Key,
                    Total = g.Count(),
                    Completed = g.Count(x => x.StatusId == 4)
                })
                .ToListAsync();

            return rows.ToDictionary(
                x => x.ProjectId,
                x => x.Total == 0 ? 0 : (int)Math.Round((double)x.Completed / x.Total * 100)
            );
        }
        [HttpGet("GetAllProjectsStatus")]
        public async Task<IActionResult> GetAllProjectStatuses()
        {
            try
            {
                var statuses = await _context.ProjectStatuses
                    .Where(t => t.IsActive)
                    .Select(t => new
                    {
                        Id = t.Id,
                        Name = t.Name,
                        Description = t.Description,
                        Active = t.IsActive,
                        CreatedAt = t.CreatedAt
                    })
                    .ToListAsync();

                return Ok(statuses);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
        // GET projects created by user
        [HttpGet("GetProjectsCreatedByMe/{accountId}")]
        public async Task<IActionResult> GetProjectsCreatedByMe(int accountId)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(accountId);
                if (account == null)
                    return NotFound("Account not found.");

                var projects = await _context.Projects
                    .AsNoTracking()
                    .Where(p => p.CreatedById == accountId && !p.IsDeleted)
                    .Select(p => new ProjectResponseDTO
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
                            .Where(m => m.AccountId == p.ProjectManagerId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        ScrumMasterId = p.ScrumMasterId,
                        ScrumMasterName = p.Members
                            .Where(m => m.AccountId == p.ScrumMasterId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        MemberNames = p.Members
                            .Where(m => m.Role == "Member" && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .ToList(),
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync();

                if (!projects.Any())
                    return NotFound("No projects found created by this account.");

                var completionMap = await GetProjectCompletionPercentageMap(projects.Select(p => p.Id));
                foreach (var p in projects)
                    p.CompletionPercentage = completionMap.TryGetValue(p.Id, out var cp) ? cp : 0;

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPost("CreateProject")]
        public async Task<IActionResult> CreateProject([FromBody] CreateProjectDTO dto, [FromQuery] int creatorId)
        {
            try
            {
                var creator = await _context.Accounts.FindAsync(creatorId);
                if (creator == null)
                    return NotFound("Creator account not found.");

                // Validate dates
                if (dto.StartDate == default)
                    return BadRequest("Start date is required.");
                if (dto.EndDate == default)
                    return BadRequest("End date is required.");
                if (dto.EndDate <= dto.StartDate)
                    return BadRequest("End date must be after start date.");

                int projectManagerId;
                int? scrumMasterId;

                if (creator.Role == "Admin")
                {
                    if (dto.ProjectManagerId == null)
                        return BadRequest("Admin must select a Project Manager.");

                    // validation pm
                    var pmExists = await _context.Accounts.AnyAsync(a => a.Id == dto.ProjectManagerId.Value);
                    if (!pmExists)
                        return BadRequest($"Account with ID {dto.ProjectManagerId} does not exist.");

                    projectManagerId = dto.ProjectManagerId.Value;
                    if (dto.IsAlsoScrumMaster)
                    {
                        scrumMasterId = projectManagerId;  // PM is also SM
                    }
                    else if (dto.ScrumMasterId.HasValue)
                    {
                        var smExists = await _context.Accounts.AnyAsync(a => a.Id == dto.ScrumMasterId.Value);
                        if (!smExists)
                            return BadRequest($"Account with ID {dto.ScrumMasterId} does not exist.");

                        scrumMasterId = dto.ScrumMasterId.Value;
                    }
                    else
                    {
                        scrumMasterId = null;  // no SM assigned
                    }
                }
                else
                {
                    projectManagerId = creatorId;
                    scrumMasterId = creatorId;

                    if (!dto.IsAlsoScrumMaster && dto.ScrumMasterId != null && dto.ScrumMasterId != creatorId)
                    {
                        var smExists = await _context.Accounts.AnyAsync(a => a.Id == dto.ScrumMasterId.Value);
                        if (!smExists)
                            return BadRequest($"Account with ID {dto.ScrumMasterId} does not exist.");

                        scrumMasterId = dto.ScrumMasterId;
                    }
                }
                foreach (var memberId in dto.MemberIds)
                {
                    var memberExists = await _context.Accounts.AnyAsync(a => a.Id == memberId);
                    if (!memberExists)
                        return BadRequest($"Account with ID {memberId} does not exist.");
                }

                // Validate no duplicate memberIds
                if (dto.MemberIds.Distinct().Count() != dto.MemberIds.Count)
                    return BadRequest("Duplicate member IDs are not allowed.");

                // Set matic to 1
                var project = new Project
                {
                    Name = dto.Name,
                    Description = dto.Description,
                    CreatedById = creatorId,
                    ProjectManagerId = projectManagerId,
                    ScrumMasterId = scrumMasterId,
                    StatusId = 1, // Not Started, then Active when PM/SM adds first task
                    StartDate = dto.StartDate,
                    EndDate = dto.EndDate,
                    CreatedAt = PhTime,
                    UpdatedAt = PhTime
                };

                _context.Projects.Add(project);
                await _context.SaveChangesAsync();

                //NOTIFFF
                if (scrumMasterId == projectManagerId || !scrumMasterId.HasValue)
                {
                    await _notificationService.NotifyAsync(
                        projectManagerId,
                        $"You have been assigned as Project Manager - Scrum Master of '{dto.Name}'.",
                        projectId: project.Id
                    );
                }
                else
                {
                    await _notificationService.NotifyAsync(
                        projectManagerId,
                        $"You have been assigned as Project Manager of '{dto.Name}'.",
                        projectId: project.Id
                    );

                    await _notificationService.NotifyAsync(
                        scrumMasterId.Value,
                        $"You have been assigned as Scrum Master of '{dto.Name}'.",
                        projectId: project.Id
                    );
                }

                foreach (var memberId in dto.MemberIds.Distinct())
                {
                    if (memberId == projectManagerId || memberId == scrumMasterId)
                        continue;

                    await _notificationService.NotifyAsync(
                        memberId,
                        $"You have been added as a Member to project '{dto.Name}'.",
                        projectId: project.Id
                    );
                }

                var pmAccount = await _context.Accounts.FindAsync(projectManagerId);
                if (pmAccount?.Email != null)
                {
                    var pmRoleLabel = (scrumMasterId.HasValue && scrumMasterId == projectManagerId)
                        ? "Project Manager & Scrum Master"
                        : "Project Manager";

                    await _emailService.SendEmailAsync(
                        pmAccount.Email,
                        $"You've been assigned to project: {dto.Name}",
                        $@"<h2>Project Assignment</h2>
                           <p>Hello <strong>{pmAccount.Name}</strong>,</p>
                           <p>You have been assigned as <strong>{pmRoleLabel}</strong> of project <strong>{dto.Name}</strong>.</p>
                           <p><strong>Start Date:</strong> {dto.StartDate:MMMM dd, yyyy}</p>
                           <p><strong>End Date:</strong> {dto.EndDate:MMMM dd, yyyy}</p>
                           <p>Please log in to view the project details.</p>"
                    );
                }

                if (scrumMasterId.HasValue && scrumMasterId != projectManagerId)
                {
                    var smEmailAccount = await _context.Accounts.FindAsync(scrumMasterId.Value);
                    if (smEmailAccount?.Email != null)
                    {
                        await _emailService.SendEmailAsync(
                            smEmailAccount.Email,
                            $"You've been assigned to project: {dto.Name}",
                            $@"<h2>Project Assignment</h2>
                           <p>Hello <strong>{smEmailAccount.Name}</strong>,</p>
                           <p>You have been assigned as <strong>Scrum Master</strong> of project <strong>{dto.Name}</strong>.</p>
                           <p><strong>Start Date:</strong> {dto.StartDate:MMMM dd, yyyy}</p>
                           <p><strong>End Date:</strong> {dto.EndDate:MMMM dd, yyyy}</p>
                           <p>Please log in to view the project details.</p>"
                        );
                    }
                }

                foreach (var memberId in dto.MemberIds.Distinct())
                {
                    if (memberId == projectManagerId || memberId == scrumMasterId)
                        continue;

                    var memberAccount = await _context.Accounts.FindAsync(memberId);
                    if (memberAccount?.Email != null)
                    {
                        await _emailService.SendEmailAsync(
                            memberAccount.Email,
                            $"You've been added to project: {dto.Name}",
                            $@"<h2>Project Member Assignment</h2>
                               <p>Hello <strong>{memberAccount.Name}</strong>,</p>
                               <p>You have been added as a <strong>Member</strong> of project <strong>{dto.Name}</strong>.</p>
                               <p><strong>Start Date:</strong> {dto.StartDate:MMMM dd, yyyy}</p>
                               <p><strong>End Date:</strong> {dto.EndDate:MMMM dd, yyyy}</p>
                               <p>Please log in to view the project details.</p>"
                        );
                    }
                }

                var pmRole = (scrumMasterId != null && scrumMasterId == projectManagerId)
                    ? "Project Manager - Scrum Master"
                    : "Project Manager";

                _context.ProjectMembers.Add(new ProjectMember
                {
                    ProjectId = project.Id,
                    AccountId = projectManagerId,
                    Role = pmRole,
                    JoinedAt = PhTime
                });

                if (scrumMasterId != null && scrumMasterId != projectManagerId)
                {
                    _context.ProjectMembers.Add(new ProjectMember
                    {
                        ProjectId = project.Id,
                        AccountId = scrumMasterId.Value,
                        Role = "Scrum Master",
                        JoinedAt = PhTime
                    });
                }

                if (dto.MemberIds.Distinct().Count() != dto.MemberIds.Count)
                    return BadRequest("Duplicate member IDs are not allowed.");

                foreach (var memberId in dto.MemberIds.Distinct())
                {
                    if (memberId == projectManagerId || memberId == scrumMasterId)
                        continue;

                    var alreadyAdded = await _context.ProjectMembers
                        .AnyAsync(m => m.ProjectId == project.Id && m.AccountId == memberId && !m.IsDeleted);

                    if (!alreadyAdded)
                    {
                        _context.ProjectMembers.Add(new ProjectMember
                        {
                            ProjectId = project.Id,
                            AccountId = memberId,
                            Role = "Member",
                            JoinedAt = PhTime
                        });
                    }
                }

                
                _context.AuditLogs.Add(new AuditLog
                {
                    ProjectId = project.Id,
                    TaskId = null,
                    AccountId = creatorId,
                    Action = "CREATED",
                    NewValue = project.Name,
                    Note = $"Created project '{project.Name}' by {creator.Name}.",
                    CreatedAt = PhTime
                });
                await _context.SaveChangesAsync();

                var smAccount = scrumMasterId.HasValue
                    ? await _context.Accounts.FindAsync(scrumMasterId.Value)
                    : null;

                var memberNames = await _context.ProjectMembers
                    .Where(pm => pm.ProjectId == project.Id && !pm.IsDeleted)
                    .Select(pm => pm.Account.Name)
                    .ToListAsync();

                return CreatedAtAction(nameof(GetProjectById), new { id = project.Id }, new ProjectResponseDTO
                {
                    Id = project.Id,
                    Name = project.Name,
                    Description = project.Description,
                    StatusId = project.StatusId,
                    StatusName = "Not Started",
                    CreatedById = project.CreatedById,
                    CreatedByName = creator.Name,
                    ProjectManagerId = project.ProjectManagerId,
                    ProjectManagerName = pmAccount?.Name,
                    ScrumMasterId = project.ScrumMasterId,
                    ScrumMasterName = smAccount?.Name,
                    MemberNames = memberNames,
                    StartDate = project.StartDate,
                    EndDate = project.EndDate,
                    CreatedAt = project.CreatedAt,
                    UpdatedAt = project.UpdatedAt
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = ex.Message,
                    inner = ex.InnerException?.Message,
                    inner2 = ex.InnerException?.InnerException?.Message
                });
            }
        }

        [HttpPatch("UpdateProject/{projectId}")]
        public async Task<IActionResult> UpdateProject(int projectId, [FromBody] UpdateProjectDTO dto, [FromQuery] int requesterId)
        {
            try
            {
                var project = await _context.Projects.FindAsync(projectId);
                if (project == null || project.IsDeleted)
                    return NotFound("Project not found.");

                var requester = await _context.Accounts.FindAsync(requesterId);
                if (requester == null)
                    return NotFound("Requester account not found.");

                var projectMember = await _context.ProjectMembers
                    .SingleOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == requesterId && !m.IsDeleted);

                var isAdmin = requester.Role == "Admin";
                var isProjectManager = projectMember?.Role == "Project Manager" ||
                                       projectMember?.Role == "Project Manager - Scrum Master";

                if (!isAdmin && !isProjectManager)
                    return StatusCode(403, "Only the Project Manager or Admin can update this project.");

                var changes = new List<string>();

                if (dto.Name != null && dto.Name != project.Name)
                {
                    changes.Add($"Name: {project.Name} → {dto.Name}");
                    project.Name = dto.Name;
                }
                if (dto.Description != null && dto.Description != project.Description)
                {
                    changes.Add($"Description updated");
                    project.Description = dto.Description;
                }

                if (dto.StatusId.HasValue && dto.StatusId != project.StatusId)
                {
                    var statusExists = await _context.ProjectStatuses.AnyAsync(s => s.Id == dto.StatusId.Value);
                    if (!statusExists)
                        return BadRequest("Invalid StatusId.");

                    changes.Add($"StatusId: {project.StatusId} → {dto.StatusId}");
                    project.StatusId = dto.StatusId.Value;
                }

                // Only Admin can update Project Manager
                if (isAdmin && dto.ProjectManagerId.HasValue && dto.ProjectManagerId != project.ProjectManagerId)
                {
                    var oldPm = await _context.ProjectMembers
                        .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == project.ProjectManagerId && !m.IsDeleted);

                    if (oldPm != null)
                    {
                        oldPm.IsDeleted = true;
                        oldPm.DeletedAt = PhTime;
                    }

                    var newPm = await _context.ProjectMembers
                        .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == dto.ProjectManagerId.Value);

                    if (newPm == null)
                    {
                        _context.ProjectMembers.Add(new ProjectMember
                        {
                            ProjectId = projectId,
                            AccountId = dto.ProjectManagerId.Value,
                            Role = "Project Manager",
                            JoinedAt = PhTime
                        });
                    }
                    else
                    {
                        newPm.IsDeleted = false;
                        newPm.DeletedAt = null;
                        newPm.Role = newPm.Role == "Scrum Master" ? "Project Manager - Scrum Master" : "Project Manager";
                        _context.Entry(newPm).State = EntityState.Modified;
                    }

                    changes.Add($"Project Manager: {project.ProjectManagerId} → {dto.ProjectManagerId}");
                    project.ProjectManagerId = dto.ProjectManagerId.Value;
                }

                // Update Scrum Master FIRST before members
                if (dto.ScrumMasterId != project.ScrumMasterId)
                {
                    if (project.ScrumMasterId.HasValue)
                    {
                        var oldSm = await _context.ProjectMembers
                            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == project.ScrumMasterId && !m.IsDeleted);

                        if (oldSm != null)
                        {
                            if (oldSm.Role == "Scrum Master")
                            {
                                oldSm.IsDeleted = true;
                                oldSm.DeletedAt = PhTime;
                            }
                            else if (oldSm.Role == "Project Manager - Scrum Master")
                                oldSm.Role = "Project Manager";

                            _context.Entry(oldSm).State = EntityState.Modified;
                        }
                    }

                    if (dto.ScrumMasterId.HasValue)
                    {
                        var newSm = _context.ProjectMembers.Local
                            .FirstOrDefault(m => m.ProjectId == projectId && m.AccountId == dto.ScrumMasterId.Value)
                            ?? await _context.ProjectMembers
                                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == dto.ScrumMasterId.Value);

                        if (newSm != null)
                        {
                            newSm.IsDeleted = false;
                            newSm.DeletedAt = null;
                            newSm.Role = newSm.Role == "Project Manager" ? "Project Manager - Scrum Master" : "Scrum Master";
                            _context.Entry(newSm).State = EntityState.Modified;
                        }
                        else
                        {
                            _context.ProjectMembers.Add(new ProjectMember
                            {
                                ProjectId = projectId,
                                AccountId = dto.ScrumMasterId.Value,
                                Role = "Scrum Master",
                                JoinedAt = PhTime
                            });
                        }
                    }

                    changes.Add($"Scrum Master: {project.ScrumMasterId} → {dto.ScrumMasterId}");
                    project.ScrumMasterId = dto.ScrumMasterId;
                }

                // Update Members AFTER SM is set
                if (dto.AssigneeIds != null)
                {
                    if (dto.AssigneeIds.Distinct().Count() != dto.AssigneeIds.Count)
                        return BadRequest("Duplicate assignee IDs are not allowed.");

                    var existingMembers = await _context.ProjectMembers
                        .Where(m => m.ProjectId == projectId && m.Role == "Member" && !m.IsDeleted)
                        .ToListAsync();

                    var toRemove = existingMembers
                        .Where(m => !dto.AssigneeIds.Contains(m.AccountId)
                            && m.AccountId != project.ProjectManagerId
                            && m.AccountId != project.ScrumMasterId)
                        .ToList();

                    foreach (var m in toRemove)
                    {
                        m.IsDeleted = true;
                        m.DeletedAt = PhTime;
                    }

                    foreach (var memberId in dto.AssigneeIds.Distinct())
                    {

                        if (memberId == project.ScrumMasterId || memberId == project.ProjectManagerId)
                            continue;

                        var existingMember = _context.ProjectMembers.Local
                            .FirstOrDefault(m => m.ProjectId == projectId && m.AccountId == memberId)
                            ?? await _context.ProjectMembers
                                .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == memberId);

                        if (existingMember == null)
                        {
                            _context.ProjectMembers.Add(new ProjectMember
                            {
                                ProjectId = projectId,
                                AccountId = memberId,
                                Role = "Member",
                                JoinedAt = PhTime
                            });
                        }
                        else if (existingMember.IsDeleted)
                        {
                            existingMember.IsDeleted = false;
                            existingMember.DeletedAt = null;
                            existingMember.Role = "Member";
                            _context.Entry(existingMember).State = EntityState.Modified;
                        }
                    }
                    changes.Add("Assignees updated");
                }

                // Validate and Update Dates
                if (dto.StartDate.HasValue && dto.EndDate.HasValue && dto.EndDate <= dto.StartDate)
                    return BadRequest("End date must be after start date.");

                if (dto.StartDate.HasValue && dto.StartDate != project.StartDate)
                {
                    changes.Add($"StartDate: {project.StartDate} → {dto.StartDate}");
                    project.StartDate = dto.StartDate.Value;
                }
                if (dto.EndDate.HasValue && dto.EndDate != project.EndDate)
                {
                    changes.Add($"EndDate: {project.EndDate} → {dto.EndDate}");
                    project.EndDate = dto.EndDate.Value;
                }

                project.UpdatedAt = PhTime;

                var requesterRole = requester.Role == "Admin" ? "Admin" : projectMember?.Role ?? "Unknown";

                if (changes.Any())
                {
                    _context.AuditLogs.Add(new AuditLog
                    {
                        ProjectId = project.Id,
                        TaskId = null,
                        AccountId = requesterId,
                        Action = "UPDATED",
                        NewValue = string.Join(", ", changes),
                        Note = $"Project updated '{project.Name}'by {requester.Name} ({requesterRole}).",
                        CreatedAt = PhTime
                    });
                }
                if (changes.Any())
                {
                    var pmAcc = await _context.Accounts.FindAsync(project.ProjectManagerId);
                    if (pmAcc?.Email != null)
                    {
                        await _emailService.SendEmailAsync(
                            pmAcc.Email,
                            $"Project Updated: {project.Name}",
                            $@"<h2>Project Update Notification</h2>
                           <p>Hello <strong>{pmAcc.Name}</strong>,</p>
                           <p>The project <strong>{project.Name}</strong> has been updated by <strong>{requester.Name}</strong>.</p>
                           <p><strong>Changes:</strong></p>
                           <ul>{string.Join("", changes.Select(c => $"<li>{c}</li>"))}</ul>
                           <p>Please log in to review the changes.</p>"
                        );
                    }

                    if (project.ScrumMasterId.HasValue && project.ScrumMasterId != project.ProjectManagerId)
                    {
                        var smAcc = await _context.Accounts.FindAsync(project.ScrumMasterId.Value);
                        if (smAcc?.Email != null)
                        {
                            await _emailService.SendEmailAsync(
                                smAcc.Email,
                                $"Project Updated: {project.Name}",
                                $@"<h2>Project Update Notification</h2>
                               <p>Hello <strong>{smAcc.Name}</strong>,</p>
                               <p>The project <strong>{project.Name}</strong> has been updated by <strong>{requester.Name}</strong>.</p>
                               <p><strong>Changes:</strong></p>
                               <ul>{string.Join("", changes.Select(c => $"<li>{c}</li>"))}</ul>
                               <p>Please log in to review the changes.</p>"
                            );
                        }
                    }
                }
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Project updated successfully.",
                    projectId = project.Id,
                    name = project.Name,
                    description = project.Description,
                    statusId = project.StatusId,
                    projectManagerId = project.ProjectManagerId,
                    scrumMasterId = project.ScrumMasterId,
                    startDate = project.StartDate,
                    endDate = project.EndDate,
                    updatedAt = project.UpdatedAt,
                    assigneeIds = await _context.ProjectMembers
                        .Where(m => m.ProjectId == projectId && m.Role == "Member" && !m.IsDeleted)
                        .Select(m => m.AccountId)
                        .ToListAsync(),
                    updatedFields = changes
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = ex.Message,
                    inner = ex.InnerException?.Message
                });
            }
        }

        // GET all projects
        [HttpGet("GetAllProjects")]
        public async Task<IActionResult> GetAllProjects()
        {
            try
            {
                var projects = await _context.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted)
                    .Select(p => new ProjectResponseDTO
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        StatusId = p.StatusId,
                        StatusName = p.Status.Name,                  //RESPONSE BODY ADDED
                        CreatedById = p.CreatedById,
                        CreatedByName = p.CreatedBy.Name,
                        ProjectManagerId = p.ProjectManagerId,
                        ProjectManagerName = p.Members
                            .Where(m => m.AccountId == p.ProjectManagerId == !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        ScrumMasterId = p.ScrumMasterId,
                        ScrumMasterName = p.Members
                            .Where(m => m.AccountId == p.ScrumMasterId == !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        MemberNames = p.Members
                            .Where(m => m.Role == "Member" && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .ToList(),
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync();

                var completionMap = await GetProjectCompletionPercentageMap(projects.Select(p => p.Id));
                foreach (var p in projects)
                    p.CompletionPercentage = completionMap.TryGetValue(p.Id, out var cp) ? cp : 0;

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // GET project by id
        [HttpGet("GetProjectById/{id}")]
        public async Task<IActionResult> GetProjectById(int id)
        {
            try
            {
                var project = await _context.Projects
                    .Where(p => p.Id == id && !p.IsDeleted)
                    .Select(p => new ProjectResponseDTO
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
                            .Where(m => m.AccountId == p.ProjectManagerId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        ScrumMasterId = p.ScrumMasterId,
                        ScrumMasterName = p.Members
                            .Where(m => m.AccountId == p.ScrumMasterId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        MemberNames = p.Members
                            .Where(m => m.Role == "Member" && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .ToList(),
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .FirstOrDefaultAsync();

                if (project == null)
                    return NotFound("Project not found.");

                project.CompletionPercentage = await GetProjectCompletionPercentage(project.Id);
                return Ok(project);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("GetMyProjects/{accountId}")]
        public async Task<IActionResult> GetMyProjects(int accountId)
        {
            try
            {
                var projects = await _context.Projects
                    .AsNoTracking()
                    .Where(p => !p.IsDeleted && p.Members.Any(m => m.AccountId == accountId))
                    .Select(p => new ProjectResponseDTO
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
                            .Where(m => m.AccountId == p.ProjectManagerId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        ScrumMasterId = p.ScrumMasterId,
                        ScrumMasterName = p.Members
                            .Where(m => m.AccountId == p.ScrumMasterId && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        MemberNames = p.Members
                            .Where(m => m.Role == "Member" && !m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .ToList(),
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        CreatedAt = p.CreatedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync();

                var completionMap = await GetProjectCompletionPercentageMap(projects.Select(p => p.Id));
                foreach (var p in projects)
                    p.CompletionPercentage = completionMap.TryGetValue(p.Id, out var cp) ? cp : 0;

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        //DELETION PROJECTS
        [HttpDelete("DeleteProject/{id}")]
        public async Task<IActionResult> DeleteProject(int id, [FromQuery] int accountId)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(accountId);
                if (account == null)
                    return NotFound("Account not found.");

                var projectMember = await _context.ProjectMembers
                    .FirstOrDefaultAsync(m => m.ProjectId == id && m.AccountId == accountId && !m.IsDeleted);

                var isAdmin = account.Role == "Admin";
                var isProjectManager = projectMember?.Role == "Project Manager" ||
                                       projectMember?.Role == "Project Manager - Scrum Master";

                if (!isAdmin && !isProjectManager)
                    return StatusCode(403, "Access denied. Admins and ProjectManagers only.");


                var project = await _context.Projects.FindAsync(id);
                if (project == null || project.IsDeleted)
                    return NotFound("Project not found.");

                project.IsDeleted = true;
                project.DeletedAt = PhTime;
                project.UpdatedAt = PhTime;

                var members = await _context.ProjectMembers
                    .Where(m => m.ProjectId == id && !m.IsDeleted)
                    .ToListAsync();
                foreach (var member in members)
                {
                    member.IsDeleted = true;
                    member.DeletedAt = PhTime;
                }

                var tasks = await _context.Tasks
                    .Where(t => t.ProjectId == id && !t.IsDeleted)
                    .ToListAsync();

                foreach (var task in tasks)
                {
                    task.IsDeleted = true;
                    task.UpdatedAt = PhTime;

                    var assignments = await _context.TaskAssignments
                        .Where(a => a.TaskId == task.Id && !a.IsDeleted)
                        .ToListAsync();

                    foreach (var assignment in assignments)
                    {
                        assignment.IsDeleted = true;
                        assignment.DeletedAt = PhTime;
                    }
                }
               
                var deleterRole = account.Role == "Admin" ? "Admin" : projectMember?.Role ?? "Unknown";

                _context.AuditLogs.Add(new AuditLog
                {
                    ProjectId = project.Id,
                    TaskId = null,
                    AccountId = accountId,
                    Action = "DELETED",
                    OldValue = project.Name,
                    Note = $"Project '{project.Name}' deleted by {account.Name} ({deleterRole}).",
                    CreatedAt = PhTime
                });

                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = "Project deleted successfully.",
                    projectId = project.Id,
                    projectName = project.Name,
                    deletedAt = project.DeletedAt,
                    deletedBy = accountId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("GetDeletedProjects")]
        public async Task<IActionResult> GetDeletedProjects()
        {
            try
            {
                var projects = await _context.Projects
                    .Where(p => p.IsDeleted)
                    .Select(p => new
                    {
                        Id = p.Id,
                        Name = p.Name,
                        Description = p.Description,
                        StatusId = p.StatusId,
                        ProjectManagerId = p.ProjectManagerId,
                        ProjectManagerName = p.Members
                            .Where(m => m.AccountId == p.ProjectManagerId && m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        ScrumMasterId = p.ScrumMasterId,
                        ScrumMasterName = p.Members
                            .Where(m => m.AccountId == p.ScrumMasterId && m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .FirstOrDefault(),
                        MemberNames = p.Members
                            .Where(m => m.Role == "Member" && m.IsDeleted)
                            .Select(m => m.Account.Name)
                            .ToList(),
                        StartDate = p.StartDate,
                        EndDate = p.EndDate,
                        DeletedAt = p.DeletedAt,
                        UpdatedAt = p.UpdatedAt
                    })
                    .ToListAsync();

                return Ok(projects);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpPatch("ReactivateProject/{projectId}")]
        public async Task<IActionResult> ReactivateProject(int projectId, [FromQuery] int accountId)
        {
            try
            {
                var account = await _context.Accounts.FindAsync(accountId);
                if (account == null)
                    return NotFound("Account not found.");

                var projectMember = await _context.ProjectMembers
                    .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.AccountId == accountId);
                
                var projectMemberRole = account.Role == "Admin" ? "Admin" : projectMember?.Role ?? "Unknown";
                
                var isAdmin = account.Role == "Admin";
                var isProjectManager = projectMember?.Role == "Project Manager" ||
                                       projectMember?.Role == "Project Manager - Scrum Master";

                if (!isAdmin && !isProjectManager)
                    return StatusCode(403, "Access denied. Admins and ProjectManagers only.");


                var project = await _context.Projects.FindAsync(projectId);
                if (project == null || !project.IsDeleted)
                    return NotFound("Project not found.");

                project.IsDeleted = false;
                project.DeletedAt = null;
                project.UpdatedAt = PhTime;

                var members = await _context.ProjectMembers
                    .Where(m => m.ProjectId == projectId && m.IsDeleted)
                    .ToListAsync();
                foreach (var member in members)
                {
                    member.IsDeleted = false;
                    member.DeletedAt = null;
                }

                var tasks = await _context.Tasks
                    .Where(t => t.ProjectId == projectId && t.IsDeleted)
                    .ToListAsync();
                foreach (var task in tasks)
                {
                    task.IsDeleted = false;
                    task.UpdatedAt = PhTime;

                    var assignments = await _context.TaskAssignments
                        .Where(a => a.TaskId == task.Id && a.IsDeleted)
                        .ToListAsync();
                    foreach (var assignment in assignments)
                    {
                        assignment.IsDeleted = false;
                        assignment.DeletedAt = null;
                    }
                }

                _context.AuditLogs.Add(new AuditLog
                {
                    ProjectId = project.Id,
                    TaskId = null,
                    AccountId = accountId,
                    Action = "RESTORED",
                    NewValue = project.Name,
                    Note = $"Project and all tasks reactivated by {account.Name} ({projectMemberRole})",
                    CreatedAt = PhTime
                });

                await _context.SaveChangesAsync();
                return Ok(new
                {
                    message = "Project reactivated successfully.",
                    projectId = project.Id,
                    restoredTasks = tasks.Count
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}