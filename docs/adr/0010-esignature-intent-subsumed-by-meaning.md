# ADR-0010 — An electronic signature's Intent is realized through Meaning + the signing ceremony, not a separate field

**Status:** Accepted · 2026-08-30

## Context
The e-signature standard the SRS references lists eight signature elements (SRS 312–319). Two of them are
adjacent and easily conflated:

- **Intent (314)** — the signer's deliberate intent to sign (as opposed to an accidental or coerced action).
- **Meaning (315)** — what the signature asserts about the record (authorship, review, approval, …).

The `ElectronicSignature` aggregate binds **Meaning** as a first-class value (`SignatureMeaning` enumeration:
`Authorship`, `Review`, `Approval`, `Verification`, `Execution`; persisted as `meaning varchar` with the
`ck_signature_meaning` CHECK) and an optional free-text **Reason** (319). It has **no distinct `intent` column**.

Verification cycle 2 (finding **SIG-14**, Minor) noted the missing distinct binding for Intent (314) and judged
the design "defensible," asking that the decision be documented (ADR/comment) or an explicit intent affirmation
be added.

## Decision
Intent (314) is **deliberately not a separate stored field**. It is realized by three mechanisms already present
in the signing ceremony:

1. **Re-authentication.** Signing requires the signer to re-enter their password at the moment of signing
   (`auth_level = "password"`, element 313). Re-supplying credentials for the specific act *is* the deliberate,
   non-repudiable expression of intent — an accidental or passive action cannot produce it.
2. **Meaning selection (315).** The signer must choose one `SignatureMeaning`; picking "Approval" vs "Review" is
   an explicit, recorded declaration of *why* they are signing, which carries the intent.
3. **Optional Reason (319).** A free-text declaration the signer may add to further qualify the act.

This mirrors how recognized e-signature regimes treat the pairing: the *meaning* component captures the
association the signer asserts, while *intent* is demonstrated by the authenticated, affirmative act of signing
rather than by a redundant "I intend to sign = true" flag.

## Consequences
- No `intent` column or migration; the schema stays aligned with what is actually verifiable. A separate boolean
  affirmation would be trivially auto-satisfiable and would add no non-repudiation value beyond the
  re-authentication that already gates signing.
- `SignatureMeaning`'s XML doc records that it carries the declared intent, and points here.
- Elements 313 (auth level, currently the constant `"password"` — see SIG-11) and 314 (intent) are coupled: the
  strength of the intent evidence is exactly the strength of the authentication ceremony. If step-up/MFA is later
  introduced (SIG-11), the intent evidence strengthens with it, at no change to this model.

## Revisit criteria
- If a regulator or customer audit requires an explicit, separately-stored intent attestation distinct from the
  Meaning and the authentication event, add an `intent`/affirmation binding to the aggregate and the sign
  ceremony and supersede this ADR.
- If authentication ever becomes non-interactive for signing (e.g. a service principal signing), revisit —
  mechanism 1 above would no longer hold and Intent would need an explicit binding.
