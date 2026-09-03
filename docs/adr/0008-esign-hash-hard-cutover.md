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
- **SIG-4** (added 2026-08-29) — the record "version" was `BitConverter.ToUInt32(SHA256(canonical))`, derived
  from the *same* input as the hash, so the version comparison was redundant with the hash and an edit-and-revert
  (A→B→A) restored both and **resurrected** an old signature though two material changes occurred. SRS line 322
  ("a material change creates a new version") was structurally unimplemented.

Correcting the formula changes the digest of every record, and replacing the pseudo-version changes the version
of every record, so signatures created under the old scheme recompute to a different hash/version and verify as
"record changed / no longer valid."

## Decision
Rewrite the canonical content to include **every material field**, serialized as **JSON** (unambiguous field
boundaries, null distinct from empty). For **SIG-4**, replace the hash-derived pseudo-version with a real
**monotonic `Complaint.ContentVersion`** (uint, starts at 1, incremented by every material mutator) rather than
the finding's suggested `xmin` — `xmin` bumps on *any* UPDATE (audit-only saves included) and would
over-invalidate, whereas a domain counter bumps only on material change and, being strictly increasing, makes
`A→B→A` unrecoverable. Validity now requires **both** hash and version to match (`StillValidFor`). Deploy as a
**hard cutover**: signatures made under the old formula/version become invalid and must be re-signed. This is acceptable because the rebuild is pre-production (no legally
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
- SIG-4 is resolved as of 2026-08-29 (migration `AddComplaintContentVersion`; `Complaint.ContentVersion`
  monotonic counter, not `xmin`). The formula-version alternative above would also apply to the version scheme
  if non-breaking upgrades are ever needed in production.
