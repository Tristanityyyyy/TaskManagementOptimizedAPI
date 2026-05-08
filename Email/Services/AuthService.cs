using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Data;
using TaskManagement.DTOs.Auth;
using TaskManagement.Exceptions;
using TaskManagement.Models;

namespace TaskManagement.Services;

public sealed class AuthService : IAuthService
{
    private const int RememberMeExpirySeconds = 7 * 24 * 60 * 60;
    private const int DefaultExpirySeconds = 60 * 60;
    private const int OtpValidityMinutes = 15;
    private const int ResetSessionMinutes = 10;
    private const string PasswordSpecials = "!@#$%^&*()_+-=[]{}|;':\",./<>?";

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    private readonly AccountDbContext _context;
    private readonly IEmailService _emailService;
    private readonly PasswordHasher<Account> _passwordHasher = new();

    public AuthService(AccountDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    // ----------------------------------------------------------------------
    // Login / logout / me
    // ----------------------------------------------------------------------

    public async Task<LoginResponseV1> LoginAsync(LoginRequestV1 dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            throw new UnauthorizedException("Invalid credentials");

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var account = await _context.Accounts
            .SingleOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

        if (account == null)
            throw new UnauthorizedException("Invalid credentials");

        if (!account.isActive)
            throw new UnauthorizedException("Your account has been deactivated. Please contact an administrator.");

        var verification = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, dto.Password);
        if (verification == PasswordVerificationResult.Failed)
            throw new UnauthorizedException("Invalid credentials");

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var expiresIn = dto.RememberMe ? RememberMeExpirySeconds : DefaultExpirySeconds;
        var now = PhTime;
        var expiry = now.AddSeconds(expiresIn);

        account.ApiToken = token;
        account.TokenExpiresAt = expiry;
        account.UpdatedAt = now;

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = account.Id,
            Action = "Logged in",
            Note = $"Account {account.Name} is logged in.",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new LoginResponseV1
        {
            Token = token,
            ExpiresIn = expiresIn,
            ExpiresAt = expiry,
            User = new AuthUserSummary
            {
                Id = account.Id,
                Name = account.Name,
                Email = account.Email,
                Role = account.Role,
                ProfilePicture = account.ProfilePicture
            }
        };
    }

    public async Task<AuthUserSummary> GetMeAsync(int requesterId, CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .AsNoTracking()
            .Where(a => a.Id == requesterId)
            .Select(a => new AuthUserSummary
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                Role = a.Role,
                ProfilePicture = a.ProfilePicture
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (account == null)
            throw new UnauthorizedException("Invalid token.");

        return account;
    }

    public async Task LogoutAsync(int requesterId, CancellationToken cancellationToken = default)
    {
        var account = await _context.Accounts
            .Where(a => a.Id == requesterId)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (account == null)
            return; // already gone — idempotent

        var now = PhTime;

        await _context.Accounts
            .Where(a => a.Id == requesterId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.ApiToken, (string?)null)
                .SetProperty(a => a.TokenExpiresAt, (DateTime?)null)
                .SetProperty(a => a.UpdatedAt, now), cancellationToken);

        _context.AuditLogs.Add(new AuditLog
        {
            AccountId = account.Id,
            Action = "Logged out",
            Note = $"Account '{account.Name}' logged out.",
            CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
    }

    // ----------------------------------------------------------------------
    // OTP / reset
    // ----------------------------------------------------------------------

    public async Task SendForgotPasswordOtpAsync(ForgotPasswordRequestV1 dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ValidationException(nameof(dto.Email), "Email is required.");

        var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

        var account = await _context.Accounts
            .Where(a => a.Email.ToLower() == normalizedEmail)
            .Select(a => new { a.Id, a.Name })
            .FirstOrDefaultAsync(cancellationToken);

        // Don't leak account existence — return success either way.
        // Security: prevents email enumeration. Email gets sent only if account exists.
        if (account == null)
            return;

        var now = PhTime;

        // Invalidate any prior unused OTPs for this account in a single SQL UPDATE.
        await _context.OtpCodes
            .Where(o => o.AccountId == account.Id && !o.IsUsed && o.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.IsUsed, true), cancellationToken);

        var otp = RandomNumberGenerator.GetInt32(100000, 999999).ToString();
        _context.OtpCodes.Add(new OtpCode
        {
            AccountId = account.Id,
            Code = otp,
            ExpiresAt = now.AddMinutes(OtpValidityMinutes),
            IsUsed = false,
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);

        await _emailService.SendOtpAsync(normalizedEmail, account.Name, otp);
    }

    public async Task VerifyOtpAsync(VerifyOtpRequestV1 dto, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();

        var accountId = await _context.Accounts
            .Where(a => a.Email.ToLower() == normalizedEmail)
            .Select(a => (int?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (accountId == null)
            throw new ValidationException("code", "Invalid OTP.");

        var otp = await _context.OtpCodes
            .Where(o => o.AccountId == accountId.Value && o.Code == dto.Code && !o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (otp == null)
            throw new ValidationException("code", "Invalid OTP.");

        if (otp.ExpiresAt < PhTime)
            throw new ValidationException("code", "OTP has expired.");

        otp.IsUsed = true;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequestV1 dto, CancellationToken cancellationToken = default)
    {
        ValidateNewPassword(dto.NewPassword);

        var normalizedEmail = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();

        var account = await _context.Accounts
            .SingleOrDefaultAsync(a => a.Email.ToLower() == normalizedEmail, cancellationToken);

        if (account == null)
            throw new NotFoundException("Account not found.");

        var verifiedOtp = await _context.OtpCodes
            .Where(o => o.AccountId == account.Id && o.IsUsed)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new { o.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        if (verifiedOtp == null)
            throw new ValidationException("otp", "OTP not verified. Please verify your OTP first.");

        if (verifiedOtp.CreatedAt < PhTime.AddMinutes(-ResetSessionMinutes))
            throw new ValidationException("otp", "Reset session expired. Please request a new OTP.");

        account.PasswordHash = _passwordHasher.HashPassword(account, dto.NewPassword);
        account.UpdatedAt = PhTime;

        // Invalidate the long-lived API token so the user has to log in again with the new password.
        account.ApiToken = null;
        account.TokenExpiresAt = null;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static void ValidateNewPassword(string? password)
    {
        var issues = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            issues.Add("Password is required.");
        }
        else
        {
            if (password.Length < 8)
                issues.Add("Password must be at least 8 characters.");
            if (!password.Any(char.IsUpper))
                issues.Add("Password must contain at least one uppercase letter.");
            if (!password.Any(char.IsLower))
                issues.Add("Password must contain at least one lowercase letter.");
            if (!password.Any(char.IsDigit))
                issues.Add("Password must contain at least one number.");
            if (!password.Any(c => PasswordSpecials.Contains(c)))
                issues.Add("Password must contain at least one special character (!@#$%^&*...).");
        }

        if (issues.Count > 0)
            throw new ValidationException("newPassword", string.Join(' ', issues));
    }
}
