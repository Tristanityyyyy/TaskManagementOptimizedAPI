using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.OpenApi.Models;
using System.Linq;
using TaskManagement.Data;
using TaskManagement.Services;
using Hangfire;
using Hangfire.SqlServer;
using TaskManagement.Jobs;
using TaskManagement.Auth;
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
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });
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

// Authentication (JWT wiring lives elsewhere; do not read Jwt:Key here — a missing key caused
// Encoding.ASCII.GetBytes(null) and immediate process exit on IIS when appsettings.json was absent.)
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddScoped<IEmailService, EmailService>(); // EMAILS
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IProjectAuthService, ProjectAuthService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<ICommentService, CommentService>();
builder.Services.AddScoped<IProjectService, ProjectService>();
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
app.UseHangfireDashboard("/hangfire");

try
{
    RecurringJob.AddOrUpdate<DueTaskWarningJob>(
        "due-task-warning",
        job => job.RunAsync(),
        "0 * * * *"); // every hour
}
catch (Exception ex)
{
    // Hangfire needs SQL Server + schema; a bad connection string or blocked DB should not take down the whole API.
    app.Logger.LogError(ex, "Hangfire recurring job registration failed.");
}

app.MapControllers();

app.Run();
