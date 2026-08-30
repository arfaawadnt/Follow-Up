# ADR-0011 — Password policy: length + complexity + deny-list; MFA deferred

**Status:** Accepted · 2026-08-31

## Context
Authentication is username + password with per-account lockout (SRS FR-1, NFR-SEC-4). Verification cycle 2
(finding **IDN-10**) noted the policy was length-only (min 8): no complexity, deny-list, breach check, or
rotation, and no MFA anywhere. The SRS names security as a concern but states no verbatim password-complexity
rule, so the strength of the policy was a deliberate product decision, not a specified requirement.

## Decision
Adopt a password policy enforced wherever a password is set (create user, change own password), centralized in
`PasswordRules.StrongPassword` so it lives in one place:

- **Length** ≥ 8.
- **Complexity**: must contain a lower-case letter, an upper-case letter, and a digit.
- **Deny-list**: reject a curated set of the most common passwords, including complexity-passing variants that a
  naive character-class rule alone would allow (e.g. `Password1`).

**MFA / step-up is deliberately deferred** for this release. The e-signature ceremony's assurance is precisely
the password re-authentication (ADR-0010); introducing MFA later strengthens both login and signature intent at
once. Rotation and breach-corpus (HIBP-style k-anonymity) checks are **not** adopted now.

## Consequences
- New passwords and password changes are held to the policy; existing stored passwords are **not** retroactively
  invalidated (no forced reset), and the seeder bypasses the command validators.
- The deny-list is a curated interim list, not exhaustive — it blocks the obvious choices, not every weak password.
- Signature `auth_level` stays the constant `"password"` (SIG-11 / `SignatureAuthLevel`) until an auth context can
  attest to a stronger level.

## Revisit criteria
- A compliance/customer requirement for MFA → add a step-up auth context, extend `SignatureAuthLevel`, and
  supersede this ADR.
- A requirement for breach-corpus rejection or rotation → replace the static deny-list with a k-anonymity breach
  check and add an expiry policy.
