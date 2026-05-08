using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Authorization;
using TaskManagement.Data;
using TaskManagement.DTOs.Account;
using TaskManagement.DTOs.Common;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class AccountsService : IAccountsService
{
    private const int MaxPageSize = 100;
    private const int MaxFileBytes = 5 * 1024 * 1024;
    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
    private static readonly Regex EmailFormat = new(@"^[^@\s]+@[^@\s]+\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private const string PasswordSpecials = "!@#$%^&*()_+-=[]{}|;':\",./<>?";

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IWebHostEnvironment _env;
    private readonly PasswordHasher<Account> _passwordHasher = new();

    public AccountsService(AccountDbContext context, IEmailService emailService, IWebHostEnvironment env)
    {
        _context = context;
        _emailService = emailService;
        _env = env;
    }

    // ----------------------------------------------------------------------
    // List & lookup
    // ----------------------------------------------------------------------

    public async Task<PagedResult<AccountListItemResponse>> ListAsync(int requesterId, string? role, bool? active,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        await EnsureRequesterExistsAsync(requesterId, cancellationToken);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        IQueryable<Account> query = _context.Accounts.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(a => a.Role == role);
        if (active.HasValue)
            query = query.Where(a => a.isActive == active.Value);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(a => a.Name)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AccountListItemResponse
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                Role = a.Role,
                Specialization = a.Specialization,
                ProfilePicture = a.ProfilePicture,
                IsActive = a.isActive
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<AccountListItemResponse>(items, total, page, pageSize);
    }

    public async Task<AccountResponse?> FindAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default)
    {
        await EnsureRequesterExistsAsync(requesterId, cancellationToken);

        return await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new AccountResponse
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                Role = a.Role,
                Specialization = a.Specialization,
                ProfilePicture = a.ProfilePicture,
                IsActive = a.isActive,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AccountResponse> GetCurrentAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAsync(requesterId, requesterId, cancellationToken);
        if (account == null)
            throw new NotFoundException("Account not found.");
        return account;
    }

    public async Task<IReadOnlyList<AccountStatsResponse>> GetUsersWithStatsAsync(int requesterId,
        CancellationToken cancellationToken = default)
    {
        await EnsureRequesterIsAdminAsync(requesterId, cancellationToken);

        // Project counts: one batched group-by, then left-joined back to users.
        var projectCounts = await _context.ProjectMembers
            .AsNoTracking()
            .GroupBy(m => m.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, cancellationToken);

        // Active task counts: status 1/2/3 means in-progress states (NotStarted, InProgress, etc.)
        var activeStatusIds = new[] { 1, 2, 3 };
        var taskCounts = await _context.TaskAssignments
            .AsNoTracking()
            .Where(a => !a.IsDeleted &&
                _context.Tasks.Any(t => t.Id == a.TaskId && !t.IsDeleted && activeStatusIds.Contains(t.StatusId)))
            .GroupBy(a => a.AccountId)
            .Select(g => new { AccountId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AccountId, x => x.Count, cancellationToken);

        var users = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Role == "User")
            .OrderBy(a => a.Name)
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Email,
                a.Role,
                a.Specialization,
                a.isActive,
                a.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return users
            .Select(u => new AccountStatsResponse
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                Specialization = u.Specialization,
                IsActive = u.isActive,
                CreatedAt = u.CreatedAt,
                ProjectCount = projectCounts.TryGetValue(u.Id, out var pc) ? pc : 0,
                ActiveTaskCount = taskCounts.TryGetValue(u.Id, out var tc) ? tc : 0
            })
            .ToList();
    }

    // ----------------------------------------------------------------------
    // Create
    // ----------------------------------------------------------------------

    public async Task<AccountResponse> CreateAsync(int requesterId, CreateAccountRequest dto,
        CancellationToken cancellationToken = default)
    {
        var admin = await EnsureRequesterIsAdminAsync(requesterId, cancellationToken);

        ValidateNewAccount(dto);

        var emailLower = dto.Email.Trim().ToLowerInvariant();
        var emailExists = await _context.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Email.ToLower() == emailLower, cancellationToken);
        if (emailExists)
            throw new ValidationException(nameof(dto.Email), "Email already exists.");

        var now = PhTime;
        var newAccount = new Account
        {
            Name = dto.Name.Trim(),
            Email = dto.Email.Trim(),
            Specialization = dto.Specialization,
            Role = dto.Role,
            isActive = dto.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        };
        newAccount.PasswordHash = _passwordHasher.HashPassword(newAccount, dto.Password);

        _context.Accounts.Add(newAccount);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "CREATED",
            NewValue = newAccount.Name,
            Note = $"User {newAccount.Name} was created by {admin.Name}.",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        await _emailService.SendAccountCreatedAsync(newAccount.Email, newAccount.Name, dto.Password);

        return new AccountResponse
        {
            Id = newAccount.Id,
            Name = newAccount.Name,
            Email = newAccount.Email,
            Role = newAccount.Role,
            Specialization = newAccount.Specialization,
            ProfilePicture = newAccount.ProfilePicture,
            IsActive = newAccount.isActive,
            CreatedAt = newAccount.CreatedAt,
            UpdatedAt = newAccount.UpdatedAt
        };
    }

    // ----------------------------------------------------------------------
    // Update
    // ----------------------------------------------------------------------

    public async Task<UpdateAccountResponse> UpdateAsync(int requesterId, int accountId, UpdateAccountRequest dto,
        CancellationToken cancellationToken = default)
    {
        var requester = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name, a.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (requester == null)
            throw new ForbiddenException("Editor account not found.");

        var existing = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (existing == null)
            throw new NotFoundException("Account not found.");

        var isAdmin = requester.Role == AppRoles.Admin;
        var isSelf = requesterId == accountId;
        if (!isAdmin && !isSelf)
            throw new ForbiddenException("You can only edit your own account.");

        var changes = new List<string>();

        if (dto.Name != null && dto.Name != existing.Name)
        {
            changes.Add("Name");
            existing.Name = dto.Name;
        }

        // Password change is only ever a self-action; admins cannot reset a user's password through this endpoint.
        var passwordTouched = dto.CurrentPassword != null
                              || dto.NewPassword != null
                              || dto.ConfirmPassword != null;
        if (passwordTouched)
        {
            if (!isSelf)
                throw new ForbiddenException("Only the account owner can change their password.");
            if (string.IsNullOrWhiteSpace(dto.CurrentPassword))
                throw new ValidationException(nameof(dto.CurrentPassword), "Current password is required.");
            if (string.IsNullOrWhiteSpace(dto.NewPassword))
                throw new ValidationException(nameof(dto.NewPassword), "New password is required.");
            if (string.IsNullOrWhiteSpace(dto.ConfirmPassword))
                throw new ValidationException(nameof(dto.ConfirmPassword), "Confirm password is required.");

            var verify = _passwordHasher.VerifyHashedPassword(existing, existing.PasswordHash, dto.CurrentPassword!);
            if (verify == PasswordVerificationResult.Failed)
                throw new ValidationException(nameof(dto.CurrentPassword), "Current password is incorrect.");

            if (dto.NewPassword != dto.ConfirmPassword)
                throw new ValidationException(nameof(dto.ConfirmPassword), "New password and confirm password do not match.");
            if (dto.CurrentPassword == dto.NewPassword)
                throw new ValidationException(nameof(dto.NewPassword), "New password must be different from the current password.");

            existing.PasswordHash = _passwordHasher.HashPassword(existing, dto.NewPassword!);
            changes.Add("Password");
        }

        if (dto.Role != null && dto.Role != existing.Role)
        {
            if (!isAdmin)
                throw new ForbiddenException("Only an admin can change a user's role.");
            changes.Add("Role");
            existing.Role = dto.Role;
        }

        if (dto.IsActive.HasValue && dto.IsActive.Value != existing.isActive)
        {
            if (!isAdmin)
                throw new ForbiddenException("Only an admin can change a user's active status.");
            changes.Add("Active Status");
            existing.isActive = dto.IsActive.Value;
        }

        if (dto.ProfilePicture != null && dto.ProfilePicture != existing.ProfilePicture)
        {
            changes.Add("Profile Picture");
            existing.ProfilePicture = dto.ProfilePicture;
        }

        if (dto.Specialization != null && dto.Specialization != existing.Specialization)
        {
            changes.Add("Specialization");
            existing.Specialization = dto.Specialization;
        }

        if (changes.Count == 0)
        {
            return new UpdateAccountResponse
            {
                Message = "No changes detected.",
                UpdatedFields = Array.Empty<string>(),
                Account = ToResponse(existing)
            };
        }

        existing.UpdatedAt = PhTime;

        var fieldSummary = changes.Count == 1
            ? changes[0]
            : string.Join(", ", changes[..^1]) + " and " + changes[^1];

        var note = isSelf
            ? $"{existing.Name} updated their own {fieldSummary}."
            : $"The {fieldSummary} of {existing.Name}'s account was updated by Admin {requester.Name}.";

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = accountId,
            Action = "UPDATED",
            NewValue = string.Join(", ", changes),
            Note = note,
            CreatedAt = PhTime
        });

        await _context.SaveChangesAsync(cancellationToken);

        var responseMessage = isSelf
            ? $"Your {fieldSummary} {(changes.Count == 1 ? "has" : "have")} been updated successfully."
            : $"The {fieldSummary} of {existing.Name}'s account {(changes.Count == 1 ? "has" : "have")} been updated successfully.";

        return new UpdateAccountResponse
        {
            Message = responseMessage,
            UpdatedFields = changes,
            Account = ToResponse(existing)
        };
    }

    // ----------------------------------------------------------------------
    // Deactivate / reactivate
    // ----------------------------------------------------------------------

    public async Task DeactivateAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default)
    {
        var admin = await EnsureRequesterIsAdminAsync(requesterId, cancellationToken);

        if (requesterId == accountId)
            throw new ValidationException("accountId", "Admins cannot deactivate their own account.");

        var existing = await _context.Accounts
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Id, a.Name, a.isActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing == null)
            throw new NotFoundException("Account not found.");

        if (!existing.isActive)
            return; // already deactivated — idempotent

        await _context.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.isActive, false), cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "DELETED",
            Note = $"User {existing.Name} was deactivated by {admin.Name}.",
            CreatedAt = PhTime
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ReactivateAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default)
    {
        var admin = await EnsureRequesterIsAdminAsync(requesterId, cancellationToken);

        var existing = await _context.Accounts
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Id, a.Name, a.isActive })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing == null)
            throw new NotFoundException("Account not found.");

        if (existing.isActive)
            return; // already active — idempotent

        await _context.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.isActive, true), cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = requesterId,
            Action = "RESTORED",
            Note = $"Account {existing.Name} reactivated by {admin.Name}.",
            CreatedAt = PhTime
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ----------------------------------------------------------------------
    // Profile picture
    // ----------------------------------------------------------------------

    public async Task<ProfilePictureResponse> RemoveProfilePictureAsync(int requesterId, int accountId,
        CancellationToken cancellationToken = default)
    {
        var requesterRole = await GetRequesterRoleAsync(requesterId, cancellationToken);
        if (requesterId != accountId && requesterRole != AppRoles.Admin)
            throw new ForbiddenException("You can only modify your own profile picture.");

        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account == null)
            throw new NotFoundException("Account not found.");

        if (string.IsNullOrEmpty(account.ProfilePicture))
            throw new ValidationException("profilePicture", "No profile picture to remove.");

        account.ProfilePicture = null;
        account.UpdatedAt = PhTime;

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = account.Id,
            Action = "DELETED",
            Note = $"Profile picture removed by {account.Name}, {account.Role}.",
            CreatedAt = PhTime
        });

        await _context.SaveChangesAsync(cancellationToken);
        return new ProfilePictureResponse(null);
    }

    public async Task<ProfilePictureResponse> UploadProfilePictureAsync(int requesterId, int accountId,
        Stream fileStream, string fileName, long fileLength,
        CancellationToken cancellationToken = default)
    {
        var requesterRole = await GetRequesterRoleAsync(requesterId, cancellationToken);
        if (requesterId != accountId && requesterRole != AppRoles.Admin)
            throw new ForbiddenException("You can only modify your own profile picture.");

        if (fileLength <= 0)
            throw new ValidationException("file", "No file uploaded.");
        if (fileLength > MaxFileBytes)
            throw new ValidationException("file", $"File too large. Max {MaxFileBytes / (1024 * 1024)} MB.");

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext))
            throw new ValidationException("file", "Only image files are allowed.");

        var account = await _context.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account == null)
            throw new NotFoundException("Account not found.");

        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploadsFolder = Path.Combine(webRoot, "uploads", "profiles");
        Directory.CreateDirectory(uploadsFolder);

        var newFileName = $"{accountId}_{Guid.NewGuid():N}{ext}";
        var filePath = Path.Combine(uploadsFolder, newFileName);

        await using (var fs = new FileStream(filePath, FileMode.Create))
        {
            await fileStream.CopyToAsync(fs, cancellationToken);
        }

        account.ProfilePicture = $"/uploads/profiles/{newFileName}";
        account.UpdatedAt = PhTime;
        await _context.SaveChangesAsync(cancellationToken);

        return new ProfilePictureResponse(account.ProfilePicture);
    }

    // ----------------------------------------------------------------------
    // Password generator (used by admin Create Account form)
    // ----------------------------------------------------------------------

    public GeneratedPasswordResponse GeneratePassword()
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string all = upper + lower + digits + PasswordSpecials;

        var random = Random.Shared;
        var chars = new List<char>
        {
            upper[random.Next(upper.Length)],
            lower[random.Next(lower.Length)],
            digits[random.Next(digits.Length)],
            PasswordSpecials[random.Next(PasswordSpecials.Length)]
        };
        for (var i = chars.Count; i < 12; i++)
            chars.Add(all[random.Next(all.Length)]);

        var password = new string(chars.OrderBy(_ => random.Next()).ToArray());
        return new GeneratedPasswordResponse(password);
    }

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    private async Task<string?> GetRequesterRoleAsync(int requesterId, CancellationToken cancellationToken)
        => await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => a.Role)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task EnsureRequesterExistsAsync(int requesterId, CancellationToken cancellationToken)
    {
        var exists = await _context.Accounts
            .AsNoTracking()
            .AnyAsync(a => a.Id == requesterId, cancellationToken);
        if (!exists)
            throw new ForbiddenException("Requester account not found.");
    }

    private async Task<(int Id, string Name)> EnsureRequesterIsAdminAsync(int requesterId,
        CancellationToken cancellationToken)
    {
        var admin = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name, a.Role })
            .FirstOrDefaultAsync(cancellationToken);
        if (admin == null || admin.Role != AppRoles.Admin)
            throw new ForbiddenException("Access denied. Admins only.");
        return (admin.Id, admin.Name);
    }

    private static void ValidateNewAccount(CreateAccountRequest dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Name))
            errors[nameof(dto.Name)] = new[] { "Name is required." };
        if (string.IsNullOrWhiteSpace(dto.Email) || !EmailFormat.IsMatch(dto.Email))
            errors[nameof(dto.Email)] = new[] { "Email must follow the format example@domain.com." };
        if (string.IsNullOrWhiteSpace(dto.Role))
            errors[nameof(dto.Role)] = new[] { "Role is required." };

        if (string.IsNullOrEmpty(dto.Password))
        {
            errors[nameof(dto.Password)] = new[] { "Password is required." };
        }
        else
        {
            var passwordIssues = new List<string>();
            if (dto.Password.Length < 8)
                passwordIssues.Add("Password must be at least 8 characters.");
            if (!dto.Password.Any(char.IsUpper))
                passwordIssues.Add("Password must contain at least one uppercase letter.");
            if (!dto.Password.Any(char.IsLower))
                passwordIssues.Add("Password must contain at least one lowercase letter.");
            if (!dto.Password.Any(char.IsDigit))
                passwordIssues.Add("Password must contain at least one number.");
            if (!dto.Password.Any(c => PasswordSpecials.Contains(c)))
                passwordIssues.Add("Password must contain at least one special character (!@#$%^&*...).");
            if (passwordIssues.Count > 0)
                errors[nameof(dto.Password)] = passwordIssues.ToArray();
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static AccountResponse ToResponse(Account a) => new()
    {
        Id = a.Id,
        Name = a.Name,
        Email = a.Email,
        Role = a.Role,
        Specialization = a.Specialization,
        ProfilePicture = a.ProfilePicture,
        IsActive = a.isActive,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt
    };
}
