using System.Net.Mime;
using System.Text.Json;
using TaskManagement.Exceptions;

namespace TaskManagement.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

            if (context.Response.HasStarted)
                throw;

            context.Response.ContentType = MediaTypeNames.Application.Json;

            switch (ex)
            {
                case NotFoundException nfe:
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = nfe.Message }));
                    return;
                case ForbiddenException fbe:
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = fbe.Message }));
                    return;
                case ValidationException vex:
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = vex.Message,
                        errors = vex.Errors
                    }));
                    return;
                default:
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "Internal server error"
                    }));
                    return;
            }
        }
    }
}
