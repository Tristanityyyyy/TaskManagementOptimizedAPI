using System.Net.Mime;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskManagement.Data;

namespace TaskManagement.Auth;

/// <summary>
/// Replaces legacy ApiKeyMiddleware + ApiTokenMiddleware: same rules — API key required except on
/// swagger + forgot/verify/reset; Bearer required except on login + forgot/verify/reset (+ swagger).
/// Attaches <see cref="ResolvedAccount"/> at <see cref="AccountHttpContextKey"/> when Bearer is validated.
/// </summary>
public sealed class TokenAuthMiddleware
{
    public const string AccountHttpContextKey = "Account";

    private readonly RequestDelegate _next;

    private static DateTime PhTime =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

    public TokenAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, AccountDbContext db)
    {
        var path = context.Request.Path;

        if (path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (path.StartsWithSegments("/api/Auth/ForgotPassword") ||
            path.StartsWithSegments("/api/Auth/VerifyOtp") ||
            path.StartsWithSegments("/api/Auth/ResetPassword"))
        {
            await _next(context);
            return;
        }

        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        if (!TryGetApiKey(context, out var providedKey) ||
            !string.Equals(providedKey, config["ApiKey"], StringComparison.Ordinal))
        {
            await WriteUnauthorizedAsync(context, "API Key missing or invalid.");
            return;
        }

        if (path.StartsWithSegments("/api/Auth/login") ||
            path.StartsWithSegments("/api/Auth/ForgotPassword") ||
            path.StartsWithSegments("/api/Auth/VerifyOtp") ||
            path.StartsWithSegments("/api/Auth/ResetPassword"))
        {
            await _next(context);
            return;
        }

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader))
        {
            await WriteUnauthorizedAsync(context, "Missing token.");
            return;
        }

        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..].Trim()
            : authHeader.Trim();

        var tracked = await db.Accounts
            .FirstOrDefaultAsync(a => a.ApiToken == token, context.RequestAborted);

        if (tracked == null)
        {
            await WriteUnauthorizedAsync(context, "Invalid token.");
            return;
        }

        if (tracked.TokenExpiresAt == null || tracked.TokenExpiresAt < PhTime)
        {
            tracked.ApiToken = null;
            tracked.TokenExpiresAt = null;
            await db.SaveChangesAsync(context.RequestAborted);

            await WriteUnauthorizedAsync(context, "Token has expired. Please log in again.");
            return;
        }

        context.Items[AccountHttpContextKey] = new ResolvedAccount
        {
            Id = tracked.Id,
            Name = tracked.Name,
            Role = tracked.Role
        };

        await _next(context);
    }

    private static bool TryGetApiKey(HttpContext context, out string key)
    {
        const string headerName = "X-Api-Key";
        if (context.Request.Headers.TryGetValue(headerName, out var headerValues))
        {
            key = headerValues.ToString();
            return !string.IsNullOrWhiteSpace(key);
        }

        if (context.Request.Query.TryGetValue("api_key", out var queryValues))
        {
            key = queryValues.ToString();
            return !string.IsNullOrWhiteSpace(key);
        }

        key = "";
        return false;
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = message }));
    }
}
