using System.Security.Cryptography;
using System.Text;

namespace FollowUp.Infrastructure.Security;

/// <summary>
/// Symmetric encryption for the few secrets stored at rest — the Oracle connection string (finding M-15) and
/// the SMTP password (finding M-16). Values are AES-GCM encrypted under a 256-bit key derived from the app's
/// master secret (domain-separated from the HMAC signing use) and carry a version prefix, so a legacy
/// plaintext value is detectable and still read transparently until it is re-encrypted. Configured once at
/// startup; when no master secret is available the protector is a pass-through (values stay as-is), so tests
/// and unconfigured environments keep working.
/// </summary>
public static class SecretProtector
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12; // AES-GCM standard nonce
    private const int TagSize = 16;
    private static byte[]? _key;

    /// <summary>Derives the encryption key from the resolved master secret. Idempotent; call once at startup.</summary>
    public static void Configure(string? masterSecret)
    {
        _key = string.IsNullOrWhiteSpace(masterSecret)
            ? null
            : SHA256.HashData(Encoding.UTF8.GetBytes("followup-secret-at-rest:" + masterSecret));
    }

    /// <summary>True when the stored value is already in the encrypted (versioned) form.</summary>
    public static bool IsProtected(string? stored) =>
        stored is not null && stored.StartsWith(Prefix, StringComparison.Ordinal);

    public static string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || _key is null || IsProtected(plaintext)) return plaintext;
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        var blob = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, blob, NonceSize + TagSize, cipher.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || _key is null || !IsProtected(stored)) return stored; // legacy plaintext / unconfigured
        var blob = Convert.FromBase64String(stored[Prefix.Length..]);
        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}
