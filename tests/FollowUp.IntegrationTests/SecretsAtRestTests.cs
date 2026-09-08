using FluentAssertions;
using FollowUp.Domain.Emailing;
using FollowUp.Domain.Integration;
using FollowUp.Infrastructure.Persistence;
using FollowUp.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FollowUp.IntegrationTests;

/// <summary>
/// Findings M-15/M-16: the Oracle connection string and the SMTP password are encrypted at rest — the raw
/// column holds ciphertext, not the secret — and are read back transparently. Existing plaintext rows are
/// re-encrypted by the startup reconcile.
/// </summary>
[Collection("integration")]
public sealed class SecretsAtRestTests
{
    private readonly IntegrationFixture _fx;
    public SecretsAtRestTests(IntegrationFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Oracle_connection_string_is_encrypted_at_rest_and_read_back_transparently()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM oracle_config;");
            var cfg = OracleConfig.Create(true, 24);
            cfg.ApplyManagedConfig("Host=oracle;User Id=svc;Password=hunter2",
                new[] { AllowListedQuery.Create("Labs", "SELECT 1") });
            db.OracleConfigs.Add(cfg);
            await db.SaveChangesAsync();
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var raw = (await db.Database.SqlQueryRaw<string>(
                "SELECT connection_string AS \"Value\" FROM oracle_config").ToListAsync()).Single();
            raw.Should().StartWith("enc:v1:");
            raw.Should().NotContain("hunter2", "the password must not be stored in plaintext");

            var cfg = await db.OracleConfigs.AsNoTracking().FirstAsync();
            cfg.ConnectionString.Should().Be("Host=oracle;User Id=svc;Password=hunter2");
        }
    }

    [SkippableFact]
    public async Task Reencryptor_encrypts_a_legacy_plaintext_secret()
    {
        Skip.IfNot(_fx.DatabaseAvailable, "FOLLOWUP_DB not set.");

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await db.Database.ExecuteSqlRawAsync("DELETE FROM smtp_config;");
            var smtp = SmtpConfig.Create();
            smtp.Configure(true, "mail", 587, true, "a@b.c", "u");
            smtp.SetPassword("PlainLegacy123");
            db.SmtpConfigs.Add(smtp);
            await db.SaveChangesAsync();
            // Simulate a row written before the converter existed: overwrite the column with raw plaintext.
            await db.Database.ExecuteSqlRawAsync("UPDATE smtp_config SET password = 'PlainLegacy123';");
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            await SecretsReencryptor.RunAsync(db);
        }

        using (var scope = _fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FollowUpDbContext>();
            var raw = (await db.Database.SqlQueryRaw<string>(
                "SELECT password AS \"Value\" FROM smtp_config").ToListAsync()).Single();
            raw.Should().StartWith("enc:v1:", "the reconcile must re-encrypt the legacy plaintext password");
            raw.Should().NotContain("PlainLegacy123");

            var smtp = await db.SmtpConfigs.AsNoTracking().FirstAsync();
            smtp.Password.Should().Be("PlainLegacy123");
        }
    }
}
