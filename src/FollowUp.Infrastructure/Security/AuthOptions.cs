using FollowUp.Application.Common.Abstractions;

namespace FollowUp.Infrastructure.Security;

/// <summary>Bound from configuration (env/appsettings). The signing secret must be supplied (NFR-SEC-3).</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string SigningSecret { get; set; } = string.Empty;
    public int MaxFailedAttempts { get; set; } = 10;
    public int LockoutMinutes { get; set; } = 15;
    public int TokenLifetimeHours { get; set; } = 10;
}

/// <summary>Adapts <see cref="AuthOptions"/> to the application's <see cref="IAuthPolicy"/>.</summary>
public sealed class AuthPolicy : IAuthPolicy
{
    private readonly AuthOptions _options;
    public AuthPolicy(AuthOptions options) => _options = options;

    public int MaxFailedAttempts => _options.MaxFailedAttempts;
    public TimeSpan LockoutWindow => TimeSpan.FromMinutes(_options.LockoutMinutes);
    public TimeSpan TokenLifetime => TimeSpan.FromHours(_options.TokenLifetimeHours);
}
