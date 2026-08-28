using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Application.Common.Exceptions;
using FollowUp.Application.Features.Auth;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Auth-security regressions (cycle-2 findings IDN-1, IDN-2). These exercise the real MediatR pipeline
/// (TransactionBehavior + IdempotencyBehavior) against the live database, which is where both defects live —
/// the unit tests pass either way because they never cross a transaction or the idempotency store.
/// </summary>
[Collection("integration")]
public sealed class AuthSecurityTests
{
    private readonly IntegrationFixture _fx;
    public AuthSecurityTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Failed_logins_persist_across_the_command_transaction_and_lock_the_account()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        // A dedicated user so we never lock the shared seeded admin that other tests authenticate as.
        const string username = "idn1-lockout-probe";
        const string password = "Correct_Horse_9!";
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var roleId = (await db.Roles.AsNoTracking().FirstAsync()).Id;
            var existing = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (existing is null)
                db.Users.Add(AppUser.Create(username, hasher.Hash(password), roleId));
            else
            {
                existing.Unlock();                          // reset any state left by a prior run
                existing.SetPassword(hasher.Hash(password));
            }
            await db.SaveChangesAsync();
        }

        // Ten bad-password logins. Each is rejected (throws), but each must persist a failed attempt —
        // without the fix the increment is rolled back with the command transaction and never accrues.
        for (var i = 0; i < 10; i++)
        {
            using var scope = _fx.Services.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var act = async () => await mediator.Send(new LoginCommand(username, "wrong", null, null));
            await act.Should().ThrowAsync<UnauthorizedException>();
        }

        // The persisted counter reflects all ten attempts and the account is now locked.
        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var user = await db.Users.AsNoTracking().FirstAsync(u => u.Username == username);
            user.FailedLoginCount.Should().BeGreaterThanOrEqualTo(10);
            user.IsLockedOut(DateTimeOffset.UtcNow).Should().BeTrue();
        }

        // Even the correct password is refused while locked — the control works end-to-end.
        using (var scope = _fx.Services.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var act = async () => await mediator.Send(new LoginCommand(username, password, null, null));
            await act.Should().ThrowAsync<UnauthorizedException>().WithMessage("*locked*");
        }
    }

    [SkippableFact]
    public async Task Login_is_excluded_from_idempotency_so_the_token_is_never_stored()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        await _fx.ResetAsync(); // clears idempotency_record (keeps the seeded admin) for a clean assertion
        const string adminPassword = "Seed_Admin_2026!";
        _fx.Idempotency.CurrentKey = "idn2-login-key";
        try
        {
            using (var scope = _fx.Services.CreateScope())
            {
                var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
                var result = await mediator.Send(new LoginCommand("admin", adminPassword, "127.0.0.1", "itest"));
                result.Token.Should().NotBeNullOrEmpty();
            }

            using (var scope = _fx.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
                (await db.IdempotencyRecords.CountAsync(r => r.RequestType == nameof(LoginCommand)))
                    .Should().Be(0, "login must be excluded from idempotency so its bearer token is never persisted");
            }
        }
        finally
        {
            _fx.Idempotency.CurrentKey = null;
        }
    }
}
