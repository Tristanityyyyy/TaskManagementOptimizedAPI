namespace TaskManagement.Exceptions;

/// <summary>
/// Maps to HTTP 401 via <see cref="Middleware.GlobalExceptionMiddleware"/>.
/// Use for credential failures and other "not authenticated" conditions where
/// <see cref="ForbiddenException"/> (403, "authenticated but not allowed") is wrong.
/// </summary>
public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}
