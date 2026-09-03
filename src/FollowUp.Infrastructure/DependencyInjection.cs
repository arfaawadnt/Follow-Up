using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
using FollowUp.Application.Features.Setup;
using FollowUp.Infrastructure.Behaviors;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Persistence.Interceptors;
using FollowUp.Infrastructure.Persistence.Outbox;
using FollowUp.Infrastructure.Persistence.Repositories;
using FollowUp.Infrastructure.Security;
using FollowUp.Infrastructure.Time;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FollowUp.Infrastructure;

/// <summary>Composition root for the Infrastructure layer — persistence, cross-cutting behaviors, auth, time.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string: ConnectionStrings:FollowUp, else FOLLOWUP_DB env var (treat empty as absent).
        var connectionString = configuration.GetConnectionString("FollowUp");
        if (string.IsNullOrWhiteSpace(connectionString))
            connectionString = Environment.GetEnvironmentVariable("FOLLOWUP_DB");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("No database connection string configured (ConnectionStrings:FollowUp or FOLLOWUP_DB).");

        // Auth options (signing secret validated at token-service construction).
        var authOptions = new AuthOptions();
        configuration.GetSection(AuthOptions.SectionName).Bind(authOptions);
        authOptions.SigningSecret = string.IsNullOrWhiteSpace(authOptions.SigningSecret)
            ? Environment.GetEnvironmentVariable("FOLLOWUP_AUTH_SECRET") ?? authOptions.SigningSecret
            : authOptions.SigningSecret;
        services.AddSingleton(authOptions);

        // Persistence — DbContext is the unit of work (ADR-0005).
        services.AddScoped<AuditAndOutboxInterceptor>();
        services.AddDbContext<FollowUpDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(sp.GetRequiredService<AuditAndOutboxInterceptor>());
        });
        services.AddScoped<IOutbox, DbOutbox>();

        // Transaction behavior wraps the handler (inside auth/validation/logging); idempotency runs inside the
        // transaction so a recorded key commits with the command's effect.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(IdempotencyBehavior<,>));
        services.TryAddScoped<IIdempotencyKeyProvider, NullIdempotencyKeyProvider>();

        // Auth / time.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAuthPolicy, AuthPolicy>();
        // Persists failed-login attempts outside the (rolled-back) login command transaction so lockout works.
        services.AddSingleton<IFailedLoginRecorder, FailedLoginRecorder>();
        services.AddSingleton<ITokenService, HmacTokenService>();

        // System principal for jobs/seeding; the API overrides ICurrentUser with an HttpContext-backed one.
        services.TryAddScoped<ICurrentUser, Security.SystemCurrentUser>();
        // No-op realtime by default; the API overrides with the SignalR implementation.
        services.TryAddScoped<IRealtimeNotifier, Notifications.NullRealtimeNotifier>();
        services.AddScoped<INotificationTemplateRepository, Notifications.NotificationTemplateRepository>();
        services.AddScoped<INotificationRecipients, Notifications.NotificationRecipients>();

        services.AddRepositories();
        services.AddQueries();
        services.AddGatewaysAndJobs();
        services.AddScoped<Persistence.Seeding.DatabaseSeeder>();
        return services;
    }

    private static IServiceCollection AddGatewaysAndJobs(this IServiceCollection services)
    {
        services.AddHttpClient();

        // Outbound gateways.
        services.AddScoped<IEmailSender, Gateways.SmtpEmailSender>();
        services.AddScoped<IWhatsAppSender, Gateways.WhatsAppSender>();
        services.AddScoped<IMapLinkResolver, Gateways.MapLinkResolver>();
        services.AddSingleton<ISpreadsheetReader, Gateways.XlsxSpreadsheetReader>();
        services.AddScoped<IRecordHasher, Gateways.RecordHasher>();
        services.AddScoped<IElectronicSignatureGate, Gateways.ElectronicSignatureGate>();
        services.AddScoped<IOracleReader, Jobs.OracleDbReader>();
        services.AddScoped<IOracleSyncRunner, Jobs.OracleSyncRunner>();

        services.AddSingleton<IFileStorage, Gateways.LocalFileStorage>();

        // Background-job orchestration services (the Hangfire jobs invoke these).
        services.AddScoped<Jobs.BoardService>();
        services.AddScoped<Application.Features.DailyBoard.Contracts.IBoardScheduler>(sp => sp.GetRequiredService<Jobs.BoardService>());
        services.AddScoped<Jobs.RetentionService>();
        services.AddScoped<IRetentionRunner>(sp => sp.GetRequiredService<Jobs.RetentionService>());
        services.AddScoped<Jobs.OutboxDispatcher>();
        return services;
    }

    private static IServiceCollection AddQueries(this IServiceCollection services)
    {
        services.AddScoped<Application.Features.Laboratories.Contracts.ILaboratoryQueries, Persistence.Queries.LaboratoryQueries>();
        services.AddScoped<Application.Features.Representatives.Contracts.IRepresentativeQueries, Persistence.Queries.RepresentativeQueries>();
        services.AddScoped<Application.Features.DailyBoard.Contracts.IDailyBoardQueries, Persistence.Queries.DailyBoardQueries>();
        services.AddScoped<Application.Features.Transfers.ITransferQueries, Persistence.Queries.TransferQueries>();
        services.AddScoped<Application.Features.LabCheckIn.ILabCheckInQueries, Persistence.Queries.LabCheckInQueries>();
        services.AddScoped<Application.Features.Outsource.IOutsourceQueries, Persistence.Queries.OutsourceQueries>();
        services.AddScoped<Application.Features.SampleTracking.ISampleTrackingQueries, Persistence.Queries.SampleTrackingQueries>();
        services.AddScoped<Application.Features.Complaints.Contracts.IComplaintQueries, Persistence.Queries.ComplaintQueries>();
        services.AddScoped<Application.Features.Marketing.IMarketingQueries, Persistence.Queries.MarketingQueries>();
        services.AddScoped<Application.Features.UserAdmin.Queries.IUserAdminQueries, Persistence.Queries.UserAdminQueries>();
        services.AddScoped<Application.Features.Setup.ISetupQueries, Persistence.Queries.SetupQueries>();
        services.AddScoped<Application.Features.Audit.IAuditQueries, Persistence.Queries.AuditQueries>();
        services.AddScoped<Application.Features.Auth.ISessionQueries, Persistence.Queries.SessionQueries>();
        services.AddScoped<Application.Features.LabStats.ILabStatsQueries, Persistence.Queries.LabStatsQueries>();
        services.AddScoped<Application.Features.TestCatalogue.ITestCatalogueQueries, Persistence.Queries.TestCatalogueQueries>();
        services.AddScoped<Application.Features.Compensation.ICompensationQueries, Persistence.Queries.CompensationQueries>();
        services.AddScoped<Application.Features.Notifications.INotificationQueries, Persistence.Queries.NotificationQueries>();
        services.AddScoped<Application.Features.Insights.IInsightsQueries, Persistence.Queries.InsightsQueries>();
        services.AddScoped<Application.Features.Setup.ISettingsQueries, Persistence.Queries.SettingsQueries>();
        return services;
    }

    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ILaboratoryRepository, LaboratoryRepository>();
        services.AddScoped<IRepresentativeRepository, RepresentativeRepository>();
        services.AddScoped<IDailyVisitRepository, DailyVisitRepository>();
        services.AddScoped<IOutsourceSampleRepository, OutsourceSampleRepository>();
        services.AddScoped<ISampleTrackingRepository, SampleTrackingRepository>();
        services.AddScoped<IMarketingVisitRepository, MarketingVisitRepository>();
        services.AddScoped<IComplaintRepository, ComplaintRepository>();
        services.AddScoped<IAppUserRepository, AppUserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        services.AddScoped<IDailyLabStatisticRepository, DailyLabStatisticRepository>();
        services.AddScoped<ITestStatisticRepository, TestStatisticRepository>();
        services.AddScoped<ITestGroupRepository, TestGroupRepository>();
        services.AddScoped<ITestSetupRepository, TestSetupRepository>();
        services.AddScoped<ILabLoyaltyLedgerRepository, LabLoyaltyLedgerRepository>();
        services.AddScoped<IRepCommissionRepository, RepCommissionRepository>();
        services.AddScoped<ICompensationConfigRepository, CompensationConfigRepository>();
        services.AddScoped<IRefItemRepository, RefItemRepository>();
        services.AddScoped<ICityRepository, CityRepository>();
        services.AddScoped<IAreaRepository, AreaRepository>();
        services.AddScoped<IAppSettingRepository, AppSettingRepository>();
        services.AddScoped<ISystemNotificationRepository, SystemNotificationRepository>();
        services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
        services.AddScoped<INotificationDeliveryLogRepository, NotificationDeliveryLogRepository>();
        services.AddScoped<IOracleConfigRepository, OracleConfigRepository>();
        services.AddScoped<IElectronicSignatureRepository, ElectronicSignatureRepository>();
        services.AddScoped<ICompensationData, CompensationData>();
        return services;
    }
}
