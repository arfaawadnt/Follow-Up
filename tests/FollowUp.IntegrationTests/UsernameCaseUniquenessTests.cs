using FluentAssertions;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FollowUp.IntegrationTests;

/// <summary>
/// IDN-7: app-level username uniqueness/lookup is case-insensitive (ToLower), but the DB unique index was
/// case-sensitive, so "Admin" and "admin" could coexist. The unique index is now functional on lower(username);
/// this proves a case-variant of an existing username is rejected at the database (SQLSTATE 23505).
/// </summary>
[Collection("integration")]
public sealed class UsernameCaseUniquenessTests
{
    private readonly IntegrationFixture _fx;
    public UsernameCaseUniquenessTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task A_username_differing_only_in_case_from_an_existing_one_is_rejected()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");
        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();

        // 'admin' is seeded; inserting 'ADMIN' must collide on the functional lower(username) unique index. The
        // failing insert persists nothing, so the test is self-contained and idempotent.
        var act = async () => await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO app_user (id, username, password_algorithm, password_iterations, password_salt, password_hash,
                      role_id, language, failed_login_count, is_active, is_built_in, created_at, created_by)
VALUES (gen_random_uuid(), 'ADMIN', 'pbkdf2', 1, 'x', 'x',
        (SELECT role_id FROM app_user WHERE lower(username) = 'admin' LIMIT 1),
        'en', 0, true, false, now(), 'test');");

        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23505"); // unique_violation
    }
}
