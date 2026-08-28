# ADR-0008 — Electronic-signature content-hash correction (hard cutover)

**Status:** Accepted · 2026-08-27

## Context
The e-signature content hash (`RecordHasher.ComputeAsync`, SRS FR-19) is the tamper-evidence digest: verifying
a record recomputes it and reports whether the record still matches the signed state. Two defects were found
(verification cycle 2, findings SIG-1 and SIG-7):

- **SIG-1** — the canonical string covered only `Number, Category, ViaChannel, AssignedTeam, Details, Status,
  Stage`. The investigation/outcome/resolution narrative (`IsValid, ValidityNotes, InvestigationNotes,
  OutcomeType, OutcomeSummary, ResolutionSummary`) and `RepresentativeId, ReceivedAt, LaboratoryId` were **not**
  hashed, so those fields could change after signing without invalidating the signature — the signature
  attested less than the record showed.
- **SIG-7** — fields were joined with an unescaped `|`, so distinct states could produce the same canonical
  string (`AssignedTeam="Ops",Details="x|y"` vs `AssignedTeam="Ops|x",Details="y"`).

Correcting the formula changes the digest of every record, so signatures created under the old formula will
recompute to a different hash and verify as "record changed / no longer valid."

## Decision
Rewrite the canonical content to include **every material field**, serialized as **JSON** (unambiguous field
boundaries, null distinct from empty). Deploy as a **hard cutover**: signatures made under the old formula
become invalid and must be re-signed. This is acceptable because the rebuild is pre-production (no legally
relied-upon signatures exist yet), and a signature that never covered the narrative fields *should* be treated
as not attesting them.

## Alternatives considered
- **Formula-version column** — store `hash_formula_version` per signature and verify each against the formula
  it was signed under, preserving old signatures for the fields they covered. Rejected for now: it adds a
  migration and dual-formula verification for no benefit while there is no production signature data. Revisit
  if signatures are ever created in a production deployment before this ships.

## Consequences
- After deploy, any pre-existing `electronic_signature` rows verify as invalid; the complaint resolve-gate
  (`esign.enforce.complaint`) will require a fresh signature. Operators must re-sign affected complaints.
- Verification is now correct: any material change (including the previously-blind narrative fields) invalidates
  the signature.

## Risks
- If a production deployment has already collected signatures, this silently invalidates them — mitigated by
  the pre-production status and this ADR; the formula-version alternative is the fallback.

## Revisit criteria
- Before the first production deployment that collects signatures, decide whether to adopt the formula-version
  column so future formula changes are non-breaking.
- Related: finding SIG-4 (a real monotonic record version via `Complaint` `xmin` instead of the hash-derived
  pseudo-version) remains open and requires a schema migration — track separately.
