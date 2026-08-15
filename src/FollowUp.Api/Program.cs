using FollowUp.Api.Auth;
using FollowUp.Api.Endpoints;
using FollowUp.Api.Middleware;
using FollowUp.Api.Realtime;
using FollowUp.Application;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Infrastructure;
using FollowUp.Infrastructure.Jobs;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Seeding;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serve on 5088 (5080 is busy on this host).
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:5088");

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/followup-.log", rollingInterval: Serilog.RollingInterval.Day, retainedFileCountLimit: 14));

// Layers.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddBackgroundJobs(builder.Configuration);

// API concerns.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>(); // overrides Infrastructure's SystemCurrentUser for HTTP
builder.Services.AddScoped<IRealtimeNotifier, FollowUp.Api.Realtime.SignalRRealtimeNotifier>(); // overrides the no-op
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? new[] { "http://localhost:4200" })
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("FollowUp"))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("FollowUp"));

var app = builder.Build();

// Self-provision: apply migrations and seed baseline (NFR-REL-1).
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
    await db.Database.MigrateAsync();
    var adminPassword = Environment.GetEnvironmentVariable("FOLLOWUP_ADMIN_PASSWORD") ?? "ChangeMe_Admin_2026!";
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    var created = await seeder.SeedAsync(adminPassword);
    if (created is not null)
        Log.Warning("Seeded built-in admin '{Admin}' — change its password immediately.", created);
}

// Pipeline (order matters — architect request-pipeline).
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseMiddleware<TokenAuthMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Health + platform (anonymous, unversioned).
app.MapHealthEndpoints();

// Versioned API surface under /api/v1 (ADR-0006).
var api = app.MapGroup("/api/v1");
api.MapAuthEndpoints();
api.MapLaboratoryEndpoints();
api.MapInsightsEndpoints();
api.MapMapsEndpoints();
api.MapOperationsEndpoints();
api.MapComplaintEndpoints();
api.MapSignatureEndpoints();
api.MapNotificationEndpoints();
api.MapUserAdminEndpoints();
api.MapSetupEndpoints();
api.MapAuditEndpoints();
api.MapCompensationEndpoints();
api.MapStatsEndpoints();
api.MapIntegrationEndpoints();

app.MapHub<NotificationsHub>("/hubs/notifications");
app.MapHangfireDashboard("/jobs", new DashboardOptions
{
    Authorization = new[] { new LocalRequestsOnlyDashboardAuthorization() },
});

// SPA fallback: serve the Angular index.html for client routes; keep default-deny for unmapped API paths.
app.MapFallback(async ctx =>
{
    var path = ctx.Request.Path.Value ?? string.Empty;
    foreach (var prefix in new[] { "/api", "/healthz", "/hubs", "/jobs", "/swagger" })
    {
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
    }
    var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
    if (File.Exists(index))
    {
        ctx.Response.ContentType = "text/html";
        await ctx.Response.SendFileAsync(index);
    }
    else
    {
        ctx.Response.StatusCode = StatusCodes.Status404NotFound;
    }
});

app.Run();

public partial class Program;
