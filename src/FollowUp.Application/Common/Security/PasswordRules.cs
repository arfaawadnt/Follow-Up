using FluentValidation;

namespace FollowUp.Application.Common.Security;

/// <summary>
/// Shared password-strength policy (finding IDN-10): minimum length + character-class complexity + a
/// common-password deny-list. Applied wherever a password is set (create user, change own password) so the
/// rule lives in one place. The rationale and the deliberate deferral of MFA are recorded in docs/adr/0011.
/// </summary>
public static class PasswordRules
{
    /// <summary>Minimum length (SRS FR-1 / NFR-SEC-4 floor).</summary>
    public const int MinLength = 8;

    // A curated deny-list of the most common passwords, including complexity-passing variants that a naive
    // character-class rule alone would still allow (e.g. "Password1"). Case-insensitive. Not exhaustive — an
    // interim guard against the obvious choices, superseded if a full breach-corpus check is introduced.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password1!", "passw0rd", "p@ssw0rd", "passw0rd!", "password123",
        "welcome1", "welcome123", "admin123", "admin@123", "administrator1", "qwerty123", "qwertyuiop1",
        "letmein1", "iloveyou1", "abc12345", "changeme1", "changeme123", "12345678", "123456789",
        "1234567890", "trustno1", "sunshine1", "monkey123", "football1", "baseball1", "dragon123",
        "master123", "shadow123", "superman1", "summer2025", "winter2025", "spring2025", "autumn2025",
    };

    /// <summary>Applies the full password policy to a string rule.</summary>
    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rb) =>
        rb.NotEmpty()
            .MinimumLength(MinLength).WithMessage($"Password must be at least {MinLength} characters.")
            .Must(HasComplexity).WithMessage("Password must include lower-case and upper-case letters and a digit.")
            .Must(NotCommon).WithMessage("That password is too common — choose a less predictable one.");

    private static bool HasComplexity(string? p) =>
        !string.IsNullOrEmpty(p) && p.Any(char.IsLower) && p.Any(char.IsUpper) && p.Any(char.IsDigit);

    private static bool NotCommon(string? p) => p is null || !Common.Contains(p.Trim());
}
