# Slice register — Electronic Signatures — audit cycle 2, 2026-08-27

Adversarial read-only audit. Rule quotes cite `architect-standard.txt` line numbers. E-signature elements are lines 311-321; "material change → new version + new signature" is line 322. Cross-refs: M-6 referenced not re-derived.

## Binding table (lines 311-321 — "bind the signature to:")

| # | Element (line) | Where bound | Status |
|---|---|---|---|
| 1 | Signer identity (312) | `signer_user_id`+`signer_username`, set server-side from ICurrentUser (ElectronicSignatures.cs:64) | VERIFIED — cannot sign as another user |
| 2 | Signer authentication level (313) | `auth_level` varchar(40) = constant literal `"password"` | VERIFIED (constant — SIG-11) |
| 3 | Intent (314) | no distinct column; subsumed by Meaning + password ceremony | MISSING distinct binding (SIG-14) |
| 4 | Meaning (315) | `meaning` varchar(32) + DB CHECK ck_signature_meaning | VERIFIED |
| 5 | Record identifier (316) | `module` varchar(50) + `record_id` varchar(100) | VERIFIED (uncanonicalized free strings — SIG-10) |
| 6 | Record version (317) | `record_version` bigint = **first 4 bytes of the content hash** | DEFECTIVE (SIG-4) |
| 7 | Timestamp (318) | `signed_at` timestamptz from IClock.UtcNow | VERIFIED |
| 8 | Reason/declaration (319) | `reason` varchar(500), optional; Meaning doubles as declaration | VERIFIED (partial) |
| 9 | Content hash (320) | `content_hash` varchar(128), server SHA-256 | VERIFIED but incomplete coverage (SIG-1) + ambiguous canonicalization (SIG-7) |
| 10 | Audit record (321) | `audit_entry` row same transaction (AuditAndOutboxInterceptor.cs:65-66,76) | VERIFIED |

Extra: `signer_ip` (ElectronicSignature.cs:50).

---

ID: SIG-1 · **Blocker** · VERIFIED
Rule: "Content hash" (320); "A material record change must create a new version and require a new signature." (322)
Location: ElectronicSignatureServices.cs:31-32; Complaint.cs:96-124; ComplaintCommands.cs:234-253
Evidence: `canonical = string.Join("|", c.Number, c.Category, c.ViaChannel, c.AssignedTeam ?? "", c.Details, c.Status.Name, c.Stage.Name)`. Unhashed but mutable: IsValid, ValidityNotes, InvestigationNotes, OutcomeType, OutcomeSummary, ResolutionSummary, RepresentativeId, ReceivedAt.
Mechanism: when the complaint is already at the target stage, re-invoking `/advance` (any UpdateComplaints holder) rewrites the narrative/outcome while `Stage.Name` — the only hashed trace — is unchanged. Hash unchanged → `StillValidFor` true → the signature stays "valid".
Impact: after an Approval signature, OutcomeType/validity notes can be swapped with no re-signature and no tamper flag; the resolve gate then closes under a stale approval. Line 322 defeated for these fields.
Fix: extend canonical content in RecordHasher.ComputeAsync to every material field, unambiguous serialization (with SIG-7). Files: ElectronicSignatureServices.cs + tests proving each field change invalidates.
Risk: changing the formula invalidates all existing signatures — needs a re-sign campaign or a formula-version column.

---

ID: SIG-2 · **Blocker** · VERIFIED
Rule: "Backend authorization" (274); "Resource-level authorization" (277); "Enforce tenant isolation across: Commands" (255-256)
Location: ElectronicSignatures.cs:17-21,50-70 (`SignRecordCommand`/handler)
Evidence: `RequiredPrivileges { get; } = Array.Empty<string>();` — handler does password re-auth + hash + Create, **no privilege check, no ScopeGuard/EnsureInScope, no signer-to-record relationship check**.
Mechanism: every complaint command enforces privilege+scope; signing that same complaint enforces neither. Any authenticated user (any role, any scope, zero complaint privileges) can create an "Approval" signature on any complaint GUID; the gate (`HasValidSignatureAsync`) checks only that *a* valid signature exists, so it satisfies ResolveComplaintHandler.
Impact: the e-signature control is bypassable — the "approval" can originate from any account incl. out-of-scope, defeating the gate's meaning. (Impersonation is NOT possible; the hole is authorization to sign at all.)
Fix: `IRecordAccessPolicy.EnsureCanSignAsync(module, recordId, meaning)`, for `complaint`: load complaint→lab→`EnsureInScope` + require a signing privilege (e.g. ResolveComplaints for Approval). Files: ElectronicSignatures.cs, new abstraction in Common/Abstractions, impl in ElectronicSignatureServices.cs, DI registration, tests in AuthAndSignatureTests.cs.
Risk: users who legitimately sign Review/Authorship but lack resolve would be blocked — the privilege-to-meaning mapping is a product decision, must be stated not invented.

---

ID: SIG-3 · **Blocker** · VERIFIED
Rule: "Enforce tenant isolation across: Commands / Queries" (255-257); "Include automated tests proving that users cannot access another tenant's records." (269)
Location: ElectronicSignatures.cs:79-105 (`VerifySignatureQuery`/handler); ServiceEndpoints.cs:43-44
Evidence: `RequiredPrivileges { get; } = Array.Empty<string>();` — `GET /esign/{module}/{recordId}` returns signer username, meaning, signed-at, still-valid for any record id with no privilege and no scope check.
Mechanism: complaint reads are scope-filtered via the lab join; this slice's read side is the one unscoped window onto org-scoped workflow data.
Impact: any authenticated user with (or guessing) complaint GUIDs outside their scope learns whether the record is signed, by whom, with what meaning, when, and whether it has changed — cross-scope disclosure of workflow state and personnel identity.
Fix: apply SIG-2's per-module record-access policy in VerifySignatureHandler (scope check; 404/403 out of scope). Files: ElectronicSignatures.cs + shared policy; tenant-isolation test.
Risk: correctly-scoped check invisible to legitimate users (panel is embedded in the complaint row they can already see).

---

ID: SIG-4 · Major · VERIFIED
Rule: "Record version" (317); "A material record change must create a new version and require a new signature." (322)
Location: ElectronicSignatureServices.cs:35-38; ElectronicSignature.cs:64-66
Evidence: `version = BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)), 0)` — first 4 bytes of the very digest whose hex is `ContentHash`. `StillValidFor(hash, version) => currentVersion == RecordVersion && currentContentHash == ContentHash`.
Mechanism: the "version" is derived from the same input as the hash → the version comparison is mathematically redundant with the hash comparison (compares the hash twice). No entity owns a monotonic version; nothing "bumps" anything. An edit-and-revert (A→B→A) restores both, **resurrecting** the old signature though two unsigned material changes occurred.
Impact: line 322 structurally unimplemented — no new version is ever created on material change; validity is point-in-time content equality, not version continuity. The `SignedVersion` shown in the UI is meaningless.
Fix: make `Complaint` `IVersioned` (uint RowVersion→xmin), have RecordHasher return the real RowVersion, keep the hash as the change discriminator. Files: Complaint.cs, complaint EF config, ElectronicSignatureServices.cs, DomainModelTests ratchet, handler tests.
Risk: existing hash-derived `record_version` never equals xmin → all pre-existing signatures read invalid; xmin changes on any UPDATE (incl. audit-only saves) → over-invalidation; fix must define whether validity = version, hash, or both.

---

ID: SIG-5 · Major · VERIFIED
Rule: "Apply zero trust and defense in depth." (271); "Address: Authentication … Session management … API abuse prevention" (272-290)
Location: ElectronicSignatures.cs:52-56 (contrast Auth.cs:67-74)
Evidence: sign re-auth does `GetByIdAsync` + `if (!_hasher.Verify(request.Password, user.Password)) throw ForbiddenException` — **no `IsLockedOut` check, no `RegisterFailedLogin`**. Login checks both.
Mechanism: (a) an attacker with a stolen session token can guess the password without tripping lockout — a lockout-bypassing oracle (throttling absence = M-6; lockout-bypass is new); (b) a locked-out account can still sign with the correct password.
Impact: FR-1/NFR-SEC-4 lockout void on this endpoint; session theft escalates to credential recovery; signatures producible by accounts the system considers locked.
Fix: check `user.IsLockedOut(_clock.UtcNow)`; on failed verify call `user.RegisterFailedLogin(...)` (inject IAuthPolicy) before throwing. Files: ElectronicSignatures.cs + tests. NOTE: for the counter to persist, the failure path must survive transaction rollback — same root as IDN-1; without that fix this is cosmetic.
Risk: mistyping while signing locks the whole account (login included) — intended but support-visible.

---

ID: SIG-6 · Major · VERIFIED
Rule: "Commands must be idempotent when they may be retried." (206); "Stable idempotency keys for retried commands" (214); "The frontend is never the security boundary." (294)
Location: ElectronicSignatures.cs:68-69; esign-panel.component.ts:89-100; snapshot :1823-1827
Evidence: handler `_signatures.Add(signature); return signature.Id.Value;`. DB has only PK + non-unique `ix_electronic_signature_module_record_id`. Grep `Idempotency` under web/src → **zero** — client never sends the header.
Mechanism: a retried sign (timeout+re-click, gateway retry, double-tap racing `busy()`) inserts a second near-identical row; the only defense is the client `busy()` flag, which line 294 disallows as the boundary.
Impact: duplicate attestations pollute a legally-meaningful append-only log; `GetLatestAsync` tie-breaks nondeterministically between same-instant duplicates.
Fix: send Idempotency-Key on `POST /esign/sign` (panel or shared ApiService interceptor); optionally short-circuit when the latest signature already matches (signer, meaning, hash, version). Files: esign-panel.component.ts or api.service.ts, optionally ElectronicSignatures.cs; idempotency test.
Risk: handler dedup must not suppress a deliberate re-sign with different meaning/reason.

---

ID: SIG-7 · Major · VERIFIED
Rule: "Content hash" (320); "A material record change must create a new version and require a new signature." (322)
Location: ElectronicSignatureServices.cs:31-33
Evidence: free-text fields joined with an unescaped `|`. `AssignedTeam="Ops", Details="x|y"` and `AssignedTeam="Ops|x", Details="y"` both canonicalize to `…|Ops|x|y|…`.
Mechanism: field-boundary ambiguity → canonical-string collisions between distinct states; a change crossing such a boundary keeps hash and signature intact; the pseudo-version (SIG-4) collides identically.
Impact: constructible tamper-evidence false negatives; an operator aware of the format can craft edits invisible to StillValidFor.
Fix: length-prefixed segments or deterministic `JsonSerializer` of a field→value map. Files: ElectronicSignatureServices.cs + tests. Do with SIG-1 in one formula revision.
Risk: any formula change invalidates all existing signatures.

---

ID: SIG-8 · Major · VERIFIED
Rule: "Optimistic concurrency for conflicting updates" (211); "Optimistic concurrency where required" (47)
Location: Complaint.cs:20 (no IVersioned); ComplaintCommands.cs:130-142 (`ResolveComplaintHandler`)
Evidence: gate reads (`HasValidSignatureAsync` recomputes hash) then `complaint.Resolve(_user.Username, _clock.UtcNow, satisfied)` commits; Complaint carries no concurrency token.
Mechanism: check-then-act TOCTOU — between the gate check and the resolve save, a concurrent advance/stage/edit can mutate a material field; resolve commits `satisfied=true` against state that no longer matches the signature. TransactionBehavior's 409 path can't fire (no token).
Impact: a complaint can be resolved as "signed" against changed content; concurrent resolve+edit interleave with no conflict surfaced.
Fix: make Complaint IVersioned (satisfies SIG-4 too); ideally re-verify the gate in the aggregate or pass the signed hash/version into Resolve for an atomic compare. Files: Complaint.cs, complaint EF config, DomainModelTests, concurrency test.
Risk: introduces 409s on complaint edits that previously silently last-write-won.

---

ID: SIG-9 · Major · VERIFIED — `POST /esign/sign` transports a plaintext `Password` in `SignBody` and carries no `RequireRateLimiting` (login does); returns `Results.Ok(new {id})` not ProblemDetails discipline; `auth_level` is a literal regardless of actual auth strength. Combined with M-6's missing throttle and SIG-5's missing lockout, a credential-guessing surface with a decorative asserted auth level. Fix: `RequireRateLimiting("login")` or dedicated "esign" policy; derive auth_level from real principal strength. Files: ServiceEndpoints.cs, ElectronicSignatures.cs. (313,302-303,294)

ID: SIG-10 · Minor · VERIFIED — `module` is a free string validated only non-empty; the only authority on valid modules is the literal `"complaint"` in RecordHasher (:23); record ids are opaque with no per-module format binding. Fix: `SignableModule` value object/Enumeration (closed set) validated in the factory. (316,182)

ID: SIG-11 · Minor · VERIFIED — `auth_level: "password"` is a hardcoded literal, not derived from the principal's real authentication (no MFA/step-up signal). Honest today but records a constant regardless of assurance; a magic string. Fix: `AuthLevel` value set populated from the auth context. (313,55)

ID: SIG-12 · Minor · VERIFIED — `POST /esign/sign` with the target in the body is not resource-oriented (form is `POST /esign/{module}/{recordId}/sign`); `Results.Ok(new {id})` is an anonymous unversioned shape. Fix: resource-action route + typed response DTO. (342-347,349)

ID: SIG-13 · Major · VERIFIED — signature test coverage is 2 cases (sign-then-verify tamper; wrong-password) in AuthAndSignatureTests.cs:70-114; no API/integration coverage. No test for unauthorized/out-of-scope signing (SIG-2), unscoped verify (SIG-3), lockout-on-sign (SIG-5), idempotency (SIG-6), or concurrency (SIG-8) — the absent categories are exactly why the holes shipped. Fix: authz/scope refusal + lockout + idempotency handler tests + API contract test. (269,439,445,446)

ID: SIG-14 · Minor · VERIFIED — the aggregate binds Meaning + optional Reason but no separate **Intent**, which the standard lists as a distinct element (314 vs 315). Design collapses Intent into Meaning + ceremony (defensible). Fix: document that Meaning subsumes Intent (ADR/comment) or add an explicit intent affirmation. (314-315)

---

## DUPLICATION
- Valid-signature evaluation duplicated across two must-stay-in-lockstep sites: VerifySignatureHandler (:100-101) and ElectronicSignatureGate.HasValidSignatureAsync (:66-72) each independently GetLatestAsync + ComputeAsync + StillValidFor. Drift hazard (Opinion-severity). Consider the gate delegating to one shared method.
- Signer re-auth in SignRecordHandler (:53-56) reimplements the verify-half of LoginHandler (Auth.cs:61-74) but **omits** the lockout arms (SIG-5) — divergent copies of an auth check.

## COVERAGE GAPS
Org-scope isolation MISSING on both SignRecordCommand (SIG-2) and VerifySignatureQuery (SIG-3). Idempotency MISSING (SIG-6). Concurrency MISSING (SIG-8). Authorization MISSING (SIG-2). Validation PARTIAL (SignRecordValidator non-emptiness only; Meaning validated late via `Enumeration.FromName` throwing → domain 400 not a validation ProblemDetails; module not a closed set SIG-10). Domain invariants PARTIAL (guards module/recordId/contentHash but not authLevel/signer presence/meaning; version defective SIG-4). Persistence MISSING uniqueness/natural-key (SIG-6); record_version stored bigint for a uint (benign widening); index non-unique (correct for append-only, but no "one latest per module+record" guarantee). Contract: anonymous `{id}` (SIG-12).

## DEFINITION OF DONE (lines 500-519)
Acceptance PARTIAL (version/tamper defective SIG-1/4/7) · Domain behavior Met but thin + version logic defective · Architecture boundaries Met · CQRS Met · Validation PARTIAL (shallow) · Backend authz **NOT met** (SIG-2 sign, SIG-3 verify) · Tenant isolation **NOT met** (SIG-2/3) · DB constraints PARTIAL (meaning CHECK + NOT NULLs; no uniqueness/idempotency SIG-6) · Indexes Met (ix supports GetLatestAsync) · Concurrency **NOT met** (SIG-8) · Idempotency **NOT met** (SIG-6) · Auditing Met (AuditEntry per insert) · Structured logging Met (passwords not logged) · Distributed tracing INFERRED Met (OTel per inventory, not verified in slice) · Standard error handling PARTIAL (anonymous shape SIG-12) · Unit tests PARTIAL (2 tests; missing authz/idempotency/concurrency SIG-13) · Integration tests **NOT met** (zero signature hits) · Security tests **NOT met** (no lockout/authz/tenant test SIG-5/2/3) · Documentation Met (BUILD-PLAN + XML docs) · Deployment/config PARTIAL (enforcement is a runtime `esign.enforce.{module}` setting; no artifact for enabling a new signable module beyond editing RecordHasher).

## OBSERVED OUTSIDE SCOPE
- VERIFIED (interacts with SIG-5 / IDN-1): TransactionBehavior only SaveChanges on the success path; on the login/sign failure path the handler throws before save, so `RegisterFailedLogin`'s mutation is rolled back — the failed-attempt counter may never advance in production for either login or sign. (This IS IDN-1; noted here for the compounding effect on SIG-5.)
- VERIFIED: AuditAndOutboxInterceptor.Serialize (:115-123) writes full before/after JSON of every audited entity. ElectronicSignature has no password column, so no secret leak here — but the interceptor serializes all non-PK properties indiscriminately; any future sensitive column on an audited aggregate would be captured (line 302 risk). Outside slice.
- VERIFIED: ResolveComplaintHandler enforces the gate decision **in the aggregate** (Complaint.Resolve(..., satisfied)), not only the handler — the residual hole is TOCTOU (SIG-8), not gate-skippability.

## VERIFIED vs ASSUMED
VERIFIED (read): all SIG-1…SIG-14; the binding table; DoD items with file citations; duplication; the web/src idempotency-absence grep; test inventory. Files: ElectronicSignature.cs, SignatureMeaning.cs, ElectronicSignatures.cs, IAuthPolicy/IElectronicSignatureGate/IElectronicSignatureRepository, ElectronicSignatureServices.cs, SupportingRepositories.cs:171-179, IntegrationAuditConfigurations.cs, AuditAndOutboxInterceptor.cs, TransactionBehavior.cs, LoggingBehavior.cs, snapshot :1759-1828, SchemaHardening (grep), ServiceEndpoints.cs:1-60, Program.cs:95-152, ComplaintCommands.cs, Complaint.cs, Auth.cs:1-99, esign-panel.component.ts, tests AuthAndSignatureTests/ResolveComplaintHandlerTests/Fakes/ComplaintTests.
INFERRED: DoD #14 distributed tracing (inventory, not observed in slice); IdempotencyBehavior/ExceptionHandlingMiddleware behavior from inventory.
ASSUMED (flagged): production effect of the TransactionBehavior rollback on the failed-login counter (mechanism verified in code; end-to-end not executed).
