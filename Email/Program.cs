using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using TaskManagement.Auth;
using TaskManagement.Data;
using TaskManagement.Services;
using Hangfire;
using TaskManagement.Jobs;
using TaskManagement.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json"
    });
});
// ----------------------------------------------------------------------
// Rate limiting
// ----------------------------------------------------------------------
// Two layers:
//   1. A GLOBAL limiter applied to every request, partitioned by the resolved account id
//      (set by TokenAuthMiddleware) → API key prefix → client IP. Skips /swagger and /hangfire.
//   2. An "auth-strict" policy attached via [EnableRateLimiting] on the abuse-prone
//      auth endpoints (login, forgot-password, verify-otp, reset-password). Per-IP, low limit,
//      defends against brute-force / OTP guessing / enumeration retries.
//
// Both limits compose: an auth call must pass BOTH the global and the strict policy.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
        }
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"Too many requests, please try again later.\"}",
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var path = httpContext.Request.Path;

        // Don't throttle Swagger UI or the Hangfire dashboard — admins need free access.
        if (path.StartsWithSegments("/swagger") || path.StartsWithSegments("/hangfire"))
            return RateLimitPartition.GetNoLimiter("nolimit");

        var partitionKey = ResolvePartitionKey(httpContext);
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });

    options.AddPolicy("auth-strict", httpContext =>
    {
        // Per-client-IP. 10 attempts per 5 minutes is enough for legitimate users
        // (forgot-password retry, OTP entry mistakes) while pinching brute force.
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter("auth-strict:" + ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });

    static string ResolvePartitionKey(HttpContext context)
    {
        // 1. Authenticated request — partition per account so a single user can't drown
        //    everyone behind a NAT.
        if (context.Items.TryGetValue(TokenAuthMiddleware.AccountHttpContextKey, out var accountObj)
            && accountObj is ResolvedAccount account)
        {
            return "acct:" + account.Id;
        }

        // 2. Anonymous but API-key-bearing request (e.g. login). Partition by a hash-ish prefix
        //    of the key so we don't log the full secret.
        if (context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
        {
            var prefix = apiKey.ToString();
            return "key:" + (prefix.Length > 8 ? prefix[..8] : prefix);
        }

        // 3. Last resort: client IP. (Includes anything that slipped past TokenAuthMiddleware,
        //    which currently is just the public-auth endpoints — they also have auth-strict.)
        return "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }
});

// Swagger configuration
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Email API",
        Version = "v1"
    });

    // Add JWT Authentication to Swagger
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
        {
            Name = "X-Api-Key",
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Description = "Enter your API Key"
        });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey, 
        In = ParameterLocation.Header,
        Description = "Enter: Bearer your-token-here"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        },
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// DbContext registration
builder.Services.AddDbContextPool<AccountDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null
        )
    )
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddScoped<IEmailService, EmailService>(); // EMAILS
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IProjectAuthService, ProjectAuthService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<INotificationsService, NotificationsService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<IAccountsService, AccountsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IStickyNotesService, StickyNotesService>();
builder.Services.AddScoped<IAuditLogsService, AuditLogsService>();
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();
builder.Services.AddScoped<DueTaskWarningJob>();

var app = builder.Build();

// Database migration (with better error handling)
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AccountDbContext>();

        // Check if database is accessible before migrating
        if (db.Database.CanConnect())
        {
            db.Database.Migrate();
            logger.LogInformation("Database migrated successfully.");
        }
        else
        {
            logger.LogWarning("Cannot connect to database. Skipping migration.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database migration failed: {Message}", ex.Message);
        // Don't throw - let the app continue without database
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Email API V1");
    c.RoutePrefix = "swagger"; // Access at /swagger
});

app.UseStaticFiles();
app.UseResponseCompression();
app.UseHttpsRedirection();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<TokenAuthMiddleware>();
// Place the limiter AFTER TokenAuthMiddleware so the global limiter can partition by
// the resolved account id, and BEFORE MapControllers so [EnableRateLimiting("auth-strict")]
// metadata is observed by the endpoint pipeline.
app.UseRateLimiter();
app.UseHangfireDashboard("/hangfire");

// Hangfire job registration is best-effort: if Hangfire's SQL schema isn't ready
// (or the DB is briefly unreachable on startup), we don't want the whole app to fail.
try
{
    RecurringJob.AddOrUpdate<DueTaskWarningJob>(
        "due-task-warning",
        job => job.RunAsync(),
        "0 * * * *"  // every hour
    );
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
    startupLogger.LogError(ex, "Failed to register recurring job 'due-task-warning'. Continuing startup.");
}

app.MapControllers();

app.Run();
