using FollowUp.Domain.Emailing;
using FollowUp.Domain.Integration;
using FollowUp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// One-time, idempotent re-encryption of secret columns that predate the encrypting converter (findings
/// M-15/M-16). Any row whose stored secret is still plaintext is loaded (read transparently) and re-saved so
/// the converter writes it back encrypted. Rows already encrypted — including the Oracle connection string,
/// which is re-provisioned from the environment on every boot — are left untouched. Runs at startup after
/// migrate + seed.
/// </summary>
public static class SecretsReencryptor
{
    public static async Task RunAsync(FollowUpDbContext db, CancellationToken ct = default)
    {
        await ReencryptOneAsync(db,
            "SELECT connection_string FROM oracle_config LIMIT 1",
            () => db.OracleConfigs.FirstOrDefaultAsync(ct),
            c => db.Entry(c).Property(nameof(OracleConfig.ConnectionString)).IsModified = true, ct);

        await ReencryptOneAsync(db,
            "SELECT password FROM smtp_config LIMIT 1",
            () => db.SmtpConfigs.FirstOrDefaultAsync(ct),
            c => db.Entry(c).Property(nameof(SmtpConfig.Password)).IsModified = true, ct);
    }

    private static async Task ReencryptOneAsync<T>(FollowUpDbContext db, string rawSql,
        Func<Task<T?>> load, Action<T> markModified, CancellationToken ct) where T : class
    {
        var raw = await ReadScalarAsync(db, rawSql, ct);
        if (string.IsNullOrEmpty(raw) || SecretProtector.IsProtected(raw)) return; // absent, empty, or already encrypted
        var entity = await load();
        if (entity is null) return;
        markModified(entity); // force the encrypting converter to run on save
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string?> ReadScalarAsync(FollowUpDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var mustOpen = conn.State != System.Data.ConnectionState.Open;
        if (mustOpen) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return await cmd.ExecuteScalarAsync(ct) as string;
        }
        finally
        {
            if (mustOpen) await conn.CloseAsync();
        }
    }
}
