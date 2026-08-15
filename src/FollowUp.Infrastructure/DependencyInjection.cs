using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Abstractions.Persistence;
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

namespace FollowUp.Infrastructure;

/// <summary>Composition root for the Infrastructure layer — persistence, cross-cutting behaviors, auth, time.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Connection string: ConnectionStrings:FollowUp, else FOLLOWUP_DB env var.
        var connectionString = configuration.GetConnectionString("FollowUp")
            ?? Environment.GetEnvironmentVariable("FOLLOWUP_DB")
            ?? throw new InvalidOperationException("No database connection string configured (ConnectionStrings:FollowUp or FOLLOWUP_DB).");

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

        // Transaction behavior runs innermost (closest to the handler), inside auth/validation/logging.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        // Auth / time.
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IAuthPolicy, AuthPolicy>();
        services.AddSingleton<ITokenService, HmacTokenService>();

        services.AddRepositories();
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
        return services;
    }
}
