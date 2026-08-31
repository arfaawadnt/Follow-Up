using FluentAssertions;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;
using FollowUp.Infrastructure.Security;

namespace FollowUp.IntegrationTests;

/// <summary>
/// IDN-8: HmacTokenService.ReadSessionId judged token expiry against wall-clock DateTimeOffset.UtcNow instead of
/// the injected IClock the rest of the slice uses, so expiry was untestable via a fake clock. It now reads IClock;
/// these tests drive expiry entirely through the injected clock (no DB, no wall-clock dependency).
/// </summary>
public sealed class TokenServiceClockTests
{
    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }
        public DateTimeOffset CairoNow => UtcNow;
        public DateOnly CairoToday => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    private static readonly AuthOptions Options = new()
    {
        SigningSecret = "idn8-test-signing-secret-value-0123456789",
        TokenLifetimeHours = 1,
    };

    [Fact]
    public void A_token_is_valid_before_and_invalid_after_expiry_as_measured_by_the_injected_clock()
    {
        var issuedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); // expires 2030-01-01T01:00Z
        var clock = new MutableClock { UtcNow = issuedAt };
        var svc = new HmacTokenService(Options, clock);

        var sessionId = new UserSessionId(Guid.NewGuid());
        var token = svc.Issue(new AppUserId(Guid.NewGuid()), sessionId, issuedAt).Token;

        // Before expiry (per the injected clock) the session id is returned.
        clock.UtcNow = issuedAt.AddMinutes(30);
        svc.ReadSessionId(token).Should().Be(sessionId);

        // Advancing only the injected clock past expiry invalidates the token — this is the assertion that fails
        // when expiry is read from wall-clock UtcNow (2030 is far in the future of any real run) rather than IClock.
        clock.UtcNow = issuedAt.AddHours(2);
        svc.ReadSessionId(token).Should().BeNull();
    }
}
