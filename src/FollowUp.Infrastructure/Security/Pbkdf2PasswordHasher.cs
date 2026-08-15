using System.Security.Cryptography;
using FollowUp.Application.Common.Abstractions;
using FollowUp.Domain.Identity;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing (SRS NFR-SEC-3): 100,000 iterations, 128-bit per-user salt, 256-bit
/// derived key, constant-time comparison.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const string Algorithm = "PBKDF2-SHA256";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;   // 128-bit
    private const int KeySize = 32;    // 256-bit

    public PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return new PasswordHash(Algorithm, Iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public bool Verify(string password, PasswordHash hash)
    {
        // Only this algorithm is supported; unknown algorithms fail closed.
        if (!string.Equals(hash.Algorithm, Algorithm, StringComparison.Ordinal))
            return false;

        var salt = Convert.FromBase64String(hash.Salt);
        var expected = Convert.FromBase64String(hash.Hash);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, hash.Iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
