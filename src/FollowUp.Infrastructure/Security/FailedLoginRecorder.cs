using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// Persists a failed-login attempt in a fresh DI scope — hence a fresh <see cref="FollowUpDbContext"/> with its
/// own connection and transaction — so the write commits even though the login command's ambient transaction is
/// rolled back by the thrown <c>UnauthorizedException</c>. Reuses the domain rule
/// <see cref="AppUser.RegisterFailedLogin"/>; no lockout logic is duplicated here. Finding IDN-1.
/// </summary>
public sealed class FailedLoginRecorder : IFailedLoginRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAuthPolicy _policy;
    private readonly IClock _clock;

    public FailedLoginRecorder(IServiceScopeFactory scopeFactory, IAuthPolicy policy, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _policy = policy;
        _clock = clock;
    }

    public async Task RecordAsync(AppUserId userId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return; // unknown user — nothing to record (and no enumeration signal)

        user.RegisterFailedLogin(_policy.MaxFailedAttempts, _policy.LockoutWindow, _clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }
}
