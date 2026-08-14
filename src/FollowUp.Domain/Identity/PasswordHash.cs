using FollowUp.Domain.Common;

namespace FollowUp.Domain.Identity;

/// <summary>
/// An opaque password hash record (PBKDF2-SHA256, per-user salt — SRS NFR-SEC-3). The Domain treats it
/// as a value; the actual derivation and constant-time verification live in Infrastructure so no crypto
/// primitive leaks into the domain model.
/// </summary>
public sealed class PasswordHash : ValueObject
{
    public string Algorithm { get; }
    public int Iterations { get; }
    public string Salt { get; }   // base64
    public string Hash { get; }   // base64

    public PasswordHash(string algorithm, int iterations, string salt, string hash)
    {
        if (string.IsNullOrWhiteSpace(salt) || string.IsNullOrWhiteSpace(hash))
            throw new DomainException("Password hash and salt are required.");
        if (iterations <= 0)
            throw new DomainException("Hash iteration count must be positive.");
        Algorithm = algorithm;
        Iterations = iterations;
        Salt = salt;
        Hash = hash;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Algorithm;
        yield return Iterations;
        yield return Salt;
        yield return Hash;
    }
}
