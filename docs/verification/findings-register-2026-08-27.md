# Findings Register — Principal-Architect Audit, Cycle 2 (2026-08-27)

Protocol: Phases 0–3 of the conformance-audit prompt. Standard = `Enterprise Application Architect.docx`
(rule quotes cite line numbers of its plain-text extraction). Baseline = `verify/baseline-20260827`.
**No file was modified during this audit** (Phase 3 stops before remediation).

Context corrections (not silently accepted):
- The audit brief says "Frontend: Angular 22". The repository is **Angular 19.2** — a recorded deviation
  from the standard's line 24 ("Angular 22"), ADR-0007 (Node 20 constraint). Carried as finding M-5.
- "Baseline for diffing: like our versions" interpreted as: diff against `verify/baseline-20260827`.
- Scope: prompt says "All included modules"; the run-notes cap one invocation at 3–6 vertical slices.
  This cycle audits **five slices** (Complaints, Compensation, Identity & Auth, Daily Board & Visits,
  E-Signatures). Sample Tracking was audited in cycle 1 (findings ST-1…ST-10, carried forward).
  Pending future cycles: Laboratories, Representatives, Marketing, Transfers, LabCheckIn, Outsource,
  Notifications, Insights/Dashboard, LabStats, TestCatalogue, Audit, Setup/Reference, Integration/Oracle,
  Maps, Angular shell/i18n.

## Phase 0 — Ground truth (every row from a command run this session; outputs quoted in evidence logs)

| Gate | Command run | Exit | Verdict | Evidence excerpt |
|---|---|---|---|---|
| 1 build | `dotnet build FollowUp.sln -c Release --no-restore -warnaserror` (fresh clone) | 0 | **PASS** | `BUILD5_OK … 0 Warning(s) 0 Error(s)` |
| 1 strict TS | `npx tsc --noEmit -p tsconfig.app.json` | 0 | **PASS** | `TSC_OK` |
| 1 SPA build | `npx ng build --configuration production` | 0 | **PASS** | `NGBUILD_OK` (+1 CommonJS warning: leaflet → m-6) |
| 1 migrations→empty DB | `dotnet ef database update` → fresh `followup_verify_g1`; re-proven on `followup_verify_p0` via self-migrating boot | 0 | **PASS** | `15 migrations applied … 34 tables` (information_schema), `P0_PROVISIONED_READY_200` |
| 1 health | boot Release dll, `curl /healthz/ready` | 0 | **PASS** | `BOOT_READY_200`, `P0_PROVISIONED_READY_200` |
| 2 architecture tests | `dotnet test tests/FollowUp.ArchitectureTests -c Release` | 0 | **PASS 21/21** | `Passed! - Failed: 0, Passed: 21` |
| 3 jscpd | `npx jscpd src web/src --min-lines 12 --min-tokens 70 …` | 0 | **PASS** | `0.84% lines / 1.36% tokens, 10 clones` (< 3%) |
| 3 dotnet format | `dotnet format FollowUp.sln --verify-no-changes` (clone) | 0 | **PASS** | `FORMAT_CLEAN_FINAL` |
| 3 eslint | — | — | **NOT CONFIGURED** | no eslint config in `web/` → finding m-7 |
| 4 backend suites | `dotnet test FollowUp.sln -c Release --no-build` on fresh `followup_verify_p0` | 0 | **PASS** | Domain 51/51 · Application 58/58 · Architecture 21/21 · Integration 18/18 |
| 4 API contract suite | `dotnet test tests/FollowUp.ApiTests` (isolated re-run) | 0 | **PASS 9/9** | solution-level run showed 4/9 + 5 SKIP — parallel-suite DB race, registered as F-201 |
| 4 Angular unit | `npx ng test --watch=false --browsers=ChromeHeadlessCI` | 0 | **PASS** | `TOTAL: 7 SUCCESS` |
| 4 e2e | — | — | **NOT RUN** | Playwright config hard-pins :5088 = live dev instance (cycle-1 incomplete item) |
| 5 OpenAPI diff | structural op-set diff, baseline vs freshly captured swagger.json | 0 | **PASS** | `baseline ops: 126 / current ops: 126 / REMOVED: none / ADDED: none` |
| 5 EF drift | `dotnet ef migrations add __DriftCheck` → inspect → remove | 0 | **PASS** | `DRIFT_CHECK_EMPTY_UP (no model drift)` |
| 5 destructive scan | grep `DropColumn|DropTable|DropForeignKey|AlterColumn` in `Up()` bodies | 0 | **PASS (reviewed)** | one data-migrating DropColumn (`LabRepReferenceParity`, UPDATE first, Down() restores); AlterColumns are widenings |
| 6 NuGet vulns | `dotnet list FollowUp.sln package --vulnerable --include-transitive` (online) | 0 | **PASS after cycle-1 pins** | only `OpenTelemetry.Api 1.9.0 Moderate GHSA-g94r-2vxg-569j` remains (accepted exception) |
| 6 npm audit | `npm audit --omit=dev` (online) | 1 | **7 High — accepted exception** | all in Angular ≤19.2.25; fix = Angular 22 (breaking; ADR-0007) → M-5 |
| 6 secret scan (history) | `git grep` credential patterns over all 91 commits | 0 | **PASS** | 36 hits triaged: CI/compose placeholders, test seeds, UI labels; no real secret. gitleaks itself unavailable on this host (Windows-container Docker) |
| 6 authz matrix | — | — | **ABSENT** | no endpoint/request × privilege test exists → finding F-202 |

Gate 1 passed → audit proceeds.

## Phase 1 — Capability inventory

Full inventory (namespaces + paths): `docs/CAPABILITY-INVENTORY.md`, produced from verified exploration.
Summary of the concerns Phase 1 asks for:

- **Shared abstractions** (all `FollowUp.Application.Common.Abstractions`): `IClock` (`UtcNow/CairoNow/CairoToday`),
  `ICurrentUser`, `IIdempotencyKeyProvider`, `IRealtimeNotifier`, `IOutbox`, `IPasswordHasher`, `ITokenService`,
  `IAuthPolicy`, `IRecordHasher`, `IEmailSender`, `IWhatsAppSender`, `IOracleReader`, `IMapLinkResolver`,
  `IFileStorage`, `ISpreadsheetReader`, `IElectronicSignatureGate`, `INotificationRecipients`, `IOracleSyncRunner`.
  No unit-of-work abstraction exists (DbContext + `TransactionBehavior` own commits — compliant with line 54).
- **Pipeline behaviors (5)**: `AuthorizationBehavior`, `ValidationBehavior`, `LoggingBehavior` (Application);
  `TransactionBehavior`, `IdempotencyBehavior` (Infrastructure). One concern each; no duplicates.
- **EF interceptors (1)**: `AuditAndOutboxInterceptor : SaveChangesInterceptor` (audit stamps + audit rows + outbox drain).
- **Value objects (9 `ValueObject` + 3 VO-like)**: `GeoLocation`, `LoyaltyTier`, `OrgScope`, `PasswordHash`,
  `AllowListedQuery`, `LabCode`, `VisitSchedule`, `TransferDetails`, `TrackingStep`; plus `Money`, `YearMonth`,
  `Enumeration` (12 derived) with dedicated converters.
- **Base classes / markers**: `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `Enumeration`, `DomainEvent`,
  `IDomainEvent`, `IHasDomainEvents`, `IAuditable`, `IVersioned`, `DomainException`; messaging markers
  `IBaseCommand`, `ICommand(<T>)`, `IQuery<T>`, `ICommandHandler`, `IQueryHandler`, `IAuthorizedRequest`,
  `DomainEventNotification`.
- **Angular cross-cutting (web/src/app/core)**: `api.service.ts` (single HTTP gateway), `auth.service.ts` +
  `auth.guard.ts` + `auth.interceptor.ts`, `error.interceptor.ts`, `i18n.ts` + `translations.ts`,
  `notification.store.ts`, `realtime.service.ts` (SignalR client), `toast.service.ts`/`toast.component.ts`,
  `ui.service.ts`, `icons.service.ts`; shared: `esign-panel.component.ts`, `map.component.ts`,
  `status-badge.pipe.ts`, `export.util.ts`.
- **Concerns with MORE THAN ONE implementation** (flag check): `ICurrentUser` (Api `CurrentUser` + Infra
  `SystemCurrentUser`), `IIdempotencyKeyProvider` (Http + Null), `IRealtimeNotifier` (SignalR + Null) — all
  three are deliberate `TryAddScoped` null-object/default pairs, documented in the inventory; no competing
  duplicate implementations of the same concern were found (Gate 3 semantic sweep).

## Phase 2 — Findings register

ID scheme: `F-2xx` = discovered during Phase 0 of this audit · `CMP-/CPN-/IDN-/BRD-/SIG-` = this cycle's
five slices · `M-x / m-x / ST-x` = cycle-1 findings carried forward (full detail in
`compliance-report-2026-08-27.md`; status re-confirmed below).

### Phase-0 discoveries

ID:          F-201
Severity:    Major
Confidence:  VERIFIED (race + silent-skip mechanism read; exact collision point INFERRED)
Rule:        "API contract tests" / "Do not claim that code compiles, migrations succeed, tests pass, or the
             solution is production-ready unless those results were actually verified." (standard lines 442, 453)
Location:    tests/FollowUp.ApiTests/ApiFixture.cs:47-64 · tests/FollowUp.IntegrationTests/IntegrationFixture.cs:54-75
Evidence:    `catch { AuthReady = false; }` around the fixture's single admin login;
             `ResetAsync()` bulk-DELETEs 11 tables (`DELETE FROM … laboratory; … audit_entry;`) on the SAME
             database. Observed: solution-level run → `ApiTests: Passed 4, Skipped 5`; isolated run → `9/9`.
Mechanism:   `dotnet test FollowUp.sln` executes test projects concurrently; both suites share one physical
             database. Any transient failure of the fixture's one-shot login (seeder collision, lock/deadlock
             with the DELETE batch, audit-trigger contention) is swallowed by the bare catch, and the five
             token-dependent contract tests silently become SKIPs.
Impact:      CI can be green while the API contract suite never actually ran — nondeterministic loss of a
             required test category with no failure signal.
Fix:         (a) point each suite at its own database (derive DB name per test assembly in both fixtures);
             (b) replace the bare catch with a loud failure when `DatabaseAvailable && !AuthReady`;
             (c) optionally serialize test projects in CI (`dotnet test -m:1`).
             Files: ApiFixture.cs, IntegrationFixture.cs, .github/workflows/ci.yml.
Risk of fix: slightly longer CI; fixtures need CREATE DATABASE rights (or two pre-provisioned DBs).

ID:          F-202
Severity:    Major
Confidence:  VERIFIED (absence — full test inventory enumerated)
Rule:        "Include automated tests proving that users cannot access another tenant's records." /
             "Authorization tests" (standard lines 269, 439; org-scope is this system's tenancy analog per ADR-0002)
Location:    tests/ (no file — nothing implements it)
Evidence:    Only privilege-level test: AuthorizationBehaviorTests (3 tests, one stubbed request). No test
             enumerates requests × privileges/roles; no org-scope dimension matrix (only Governorate+Segment
             proven in ReadPathTests).
Mechanism:   The authorization surface (`IAuthorizedRequest.RequiredPrivileges` on 120+ requests and 6-dimension
             `OrgScope`) has no completeness check; a request shipping with wrong/empty privileges is invisible.
Impact:      A privilege regression (e.g., a new command with empty RequiredPrivileges) reaches production
             undetected; M-2 is exactly this class, caught only by manual audit.
Fix:         Data-driven test: enumerate every ICommand/IQuery, assert its expected privilege set from an
             explicit matrix (new request absent from the matrix ⇒ fail); plus one deny-path test per
             org-scope dimension. New file tests/FollowUp.Application.Tests/Security/AuthorizationMatrixTests.cs.
Risk of fix: none to production code; the matrix must be reviewed once by a human.

### Carried forward from cycle 1 (all still open; evidence in compliance-report-2026-08-27.md)

| ID | Sev | Confidence | One-line status |
|---|---|---|---|
| M-1 | Major | VERIFIED | 46/75 commands without validators (ratchet-pinned) |
| M-2 | Major | VERIFIED | GetLaboratories/GetLaboratoryById lack IAuthorizedRequest (ratchet-pinned) |
| M-3 | Major | VERIFIED | Jobs bypass MediatR pipeline; contradicts ADR-0004's own text |
| M-4 | Major | VERIFIED | UTC business-"today" in 5 read queries (Cairo contract) |
| M-5 | Major | VERIFIED | Angular 19.2: 7 High advisories; fix is breaking (ADR-0007) |
| M-6 | Major | VERIFIED | Signature endpoint = unthrottled password oracle; admin-password literal fallback |
| m-1…m-8 | Minor | VERIFIED | nextcode repo-in-endpoint · config reads via repos · `Clients.All` broadcast · MapLinkResolver ad-hoc HttpClient · lab-form 114-line clone · leaflet CJS warning · no ESLint · dashboard loopback filter |
| ST-1 | Major | VERIFIED | VisitReceived after rollover silently dropped from derived tracking |
| ST-2 | Major | INFERRED (binding) | Batch entry `{}` → NRE → 500 (validator gap made concrete) |
| ST-3…ST-6 | Minor | VERIFIED | manual ValidationException · "Received" literal · unscoped trackingRows fetch · outbox batch-commit persists failed handlers' partial writes |
| ST-7…ST-10 | Opinion | VERIFIED | commands-as-wire-contracts · free-text audit usernames · no concurrency token on SampleTracking · unbounded report range |

### Slice findings (full detail in `docs/verification/slices/<slice>.md`)

Per-slice registers written this cycle: [complaints.md](slices/complaints.md), [compensation.md](slices/compensation.md),
[identity-auth.md](slices/identity-auth.md), [esignatures.md](slices/esignatures.md), [daily-board.md](slices/daily-board.md).
Every finding was labelled VERIFIED by the slice auditor from quoted code; the 11 Blockers were additionally
re-verified by the lead (me) reading the cited lines directly (confirmation notes after the table).

**Totals this cycle: 11 Blockers · 38 Majors · 32 Minors · 9 Opinions** (across 5 slices + Phase-0 + carried ST).

| Slice | Blockers | Majors | Minors | Opinions |
|---|---|---|---|---|
| Complaints (CMP) | CMP-1, CMP-2 | CMP-3…CMP-10 (8) | CMP-11…CMP-19 (9) | CMP-20, CMP-21 |
| Compensation (CPN) | CPN-1, CPN-2, CPN-3 | CPN-4…CPN-11 (8) | CPN-12…CPN-17 (6) | CPN-18 |
| Identity & Auth (IDN) | IDN-1, IDN-2 | IDN-3, IDN-4, IDN-5 | IDN-6, IDN-7, IDN-8 | IDN-9, IDN-10 |
| E-Signatures (SIG) | SIG-1, SIG-2, SIG-3 | SIG-4…SIG-9, SIG-13 (7) | SIG-10, SIG-11, SIG-12, SIG-14 | — |
| Daily Board (BRD) | BRD-1 | BRD-2…BRD-9 (8) | BRD-10, BRD-11, BRD-12 | — |
| Sample Tracking (ST, cyc.1) | — | ST-1, ST-2 | ST-3…ST-6 | ST-7…ST-10 |
| Phase 0 | — | F-201, F-202 | — | — |

### Blocker roster (11 confirmed, 5 slices) — lead re-verification notes

| ID | Blocker | Lead confirmation (code read directly) |
|---|---|---|
| IDN-1 | Account lockout non-functional: `LoginHandler` increments `FailedLoginCount` in memory then throws; `TransactionBehavior` only `SaveChangesAsync` **after** `next()` returns, so the throw rolls it back | CONFIRMED — `LoginCommand : ICommand<LoginResult>` (an IBaseCommand); TransactionBehavior.cs:40-44 saves after `next()`; only `DbUpdateConcurrencyException` caught |
| IDN-2 | Login `LoginResult` (raw token) serialized into `idempotency_record.response_json` in cleartext when an `Idempotency-Key` header is present | CONFIRMED — IdempotencyBehavior.cs:36-53 acts on any IBaseCommand + non-blank key; HttpIdempotencyKeyProvider reads the header ungated; login is anonymous (latent — needs a key on the login call) |
| CMP-1 | Complaint by-id + audit-trail reads take no `OrgScope`; any ViewComplaints holder reads any complaint + its audit JSON cross-scope | CONFIRMED — ComplaintMarketingQueries.cs: `SearchAsync(…, OrgScope, …)` scopes; `GetByIdAsync(Guid, bool, ct)` and `GetAuditAsync(Guid, ct)` do not |
| CMP-2 | Complaint stage machine is an unguarded setter; stages forgeable out of order / on resolved records / without the resolve privilege | CONFIRMED — `MoveToStage(stage) => Stage = stage;` (Complaint.cs:161); `ComplaintStage` is a plain Enumeration with no transition map; `MoveComplaintStageCommand` needs only UpdateComplaints |
| CPN-1 | Loyalty ledger read `GetLabLedgerAsync(Guid labId, ct)` unscoped while siblings scope | CONFIRMED — StatsCompensationQueries.cs:114 vs :90/:100 which take+apply OrgScope |
| CPN-2 | Commissions read accepts `OrgScope scope` but never applies it → every rep's salary returns to any ManageCommissions holder cross-scope | CONFIRMED — GetCommissionsAsync(:121) has the param; no `ApplyScope` in its body; reps filtered only by `IsActive` |
| CPN-3 | Commission save has no resource-level authorization (no scope/ownership check after load) | CONFIRMED — SaveCommissionHandler (Compensation.cs:189-211) has no EnsureInScope; ScopeGuard has no Representative overload |
| SIG-1 | Signature content hash omits mutable material fields (validity/investigation/outcome/resolution) → post-sign edits don't invalidate | CONFIRMED (subagent quote of the canonical string; consistent with the Complaints-slice CMP-4) |
| SIG-2 | Any authenticated user can sign any record: `SignRecordCommand.RequiredPrivileges = Array.Empty`, no scope guard | CONFIRMED — ElectronicSignatures.cs:20 empty privileges; no EnsureInScope/ScopeGuard in the file |
| SIG-3 | Signature verify query unscoped and unprivileged → cross-scope disclosure of signer/meaning/state | CONFIRMED — ElectronicSignatures.cs:81 empty privileges; VerifySignatureHandler applies no scope |
| BRD-1 | Midnight roll-over crashes and poisons: `RollToMonthlyAsync` DB-queries (not `.Local`) inside the per-visit loop → duplicate `MonthlySample` violates the unique index → board generation stops | CONFIRMED — BoardService.cs:143 uses `FirstOrDefaultAsync` on the DbSet; called in the archive loop; StatisticsConfigurations.cs:21 `HasIndex(LaboratoryId, Period).IsUnique()`; the failing SaveChanges is the one that persists today's board |

A twelfth org-scope read leak of the same family, **BRD-5** (unscoped `GetSuggestedSampleCountAsync`), was rated
Major by the auditor only because it exposes a single sample count rather than a full record — it is fixed by
the same Group 1 pattern. Full evidence/mechanism/fix for every ID is in the per-slice files. Carried-forward cycle-1 findings
(M-1…M-6, m-1…m-8, ST-1…ST-10) and Phase-0 discoveries (F-201 test-race, F-202 no authz matrix) are above.

## Phase 3 — Stop

**Nothing has been modified.** Remediation begins only on explicit approval (Phase 4).

### Remediation order — Blockers first, grouped by shared fix, blast radius ascending

The 11 Blockers cluster into five fix-families. Order reflects "cleanest + highest-severity first":

**Group 0 — BRD-1 (roll-over poison).** The single highest operational-impact Blocker with the smallest,
lowest-risk fix: one `.Local`-first lookup in `RollToMonthlyAsync` (the exact pattern already used in
`SampleTrackingRepository.GetByAreaDateAsync`), no schema/contract change, plus a rollover integration test
with two verified visits on one lab/day (which would have caught it). **Recommend fixing first** — every
night a single lab has two received visits, board generation currently stops.

**Group 1 — SCOPE-READ leakage (CMP-1, CPN-1, CPN-2, SIG-3, + BRD-5).** The most valuable batch: five
org-scope read leaks, one of them salary data (CPN-2). Uniform fix — thread `OrgScope` into the read
interface, `ApplyScope`/join on the scoped lab set (or `EnsureInScope` in the handler), pass `_user.Scope`.
~2-4 files each, **no schema change, no external contract change** (the scope arg is internal). Each ships
with the org-scope isolation test the standard mandates (line 269) and that F-202 flags as absent.
Blast radius: small. **Recommended to approve and fix alongside Group 0.**

**Group 2 — Missing write authorization (SIG-2, CPN-3, CMP-2).** SIG-2 and CPN-3 add a resource-access
check (a signing policy; a `ScopeGuard` Representative overload) — self-contained. CMP-2 is larger: it needs
a real `ComplaintStage` transition map + `EnsureNotResolved` guards, **changes an existing test** that pins
the unguarded setter (`MoveToStage_never_changes_status`), and needs a **domain-owner decision** on the legal
reopen→re-investigate edge. SIG-2's privilege-to-meaning mapping is also a product decision.

**Group 3 — Auth/session security (IDN-1, IDN-2).** IDN-1: persist the failed-login increment outside the
rolled-back command transaction (or return a typed failure and map at the endpoint) — must preserve the
uniform login error. IDN-2: exclude anonymous/auth commands from idempotency response capture. Both
self-contained; IDN-1 needs an integration test that asserts the counter persists across separate requests
(the current unit test passes while production is broken).

**Group 4 — E-signature integrity (SIG-1, + the Major cluster SIG-4/7/8 and CMP-4).** ESCALATION — see below.

After the Blockers: the Majors by blast radius — the missing-domain-guard Majors (BRD-6 SetVerified, BRD-7
collector check, CMP-3 fail-open Resolve: small, self-contained, high value); the roll-over integrity Majors
(BRD-3 single-transaction, BRD-4 schedulable-status duplication); the duplication/hardcoding Majors
(CMP-5/8, CPN-4/8, BRD-11/12); the auth-security Majors (IDN-5 session revoke, SIG-5 lockout-on-sign — both
depend on IDN-1 landing first); then the concurrency tokens (BRD-2, IDN-4, CPN-9, CMP-6, and Complaint for
SIG-4/8: add `IVersioned`, one migration each — schema-gated, see below); then Minors, with the DB
constraint/index Minors (BRD-9, CMP-11, CPN-13, CMP-10/12) batchable into one migration.

### Findings whose fix needs explicit approval BEFORE I touch them (per the protocol's escalation rules)

- **Changes a public API contract** (needs a versioned decision, ADR-0006 path): CMP-5 (retire/redirect the
  `/complaints/{id}/stage` route), CMP-13 (`POST /complaints` response shape), CPN-15 (compensation response
  DTOs), SIG-12 (`/esign/sign` → resource-action route), and any concurrency-token version exposed in a DTO.
- **Requires a database migration**: every `IVersioned` addition (BRD-2, CMP-6, IDN-4, CPN-9, and `Complaint`
  for SIG-4/8), CHECK constraints (CMP-11, CPN-13), unique indexes (BRD-9 `daily_visit(lab,date,time)`), the
  FK changes (CMP-10 cascade→restrict, CMP-12 rep FK), case-insensitive username (IDN-7), signature uniqueness
  (SIG-6). Of these, **destructive / needs data review before the migration can even apply**: CMP-10
  (cascade→restrict), IDN-7 (unique-index rebuild), and BRD-9 (unique index fails if a duplicate `(lab,date,
  time)` already exists) — I will not run these without a go.
- **Changes an existing test's expectations**: CMP-2 (`MoveToStage_never_changes_status`), CMP-3 (removing
  the fail-open `Resolve` default breaks ComplaintTests.cs:34,64), SIG-1/4/7 (invalidate existing signatures →
  AuthAndSignatureTests).

### Escalations — decisions only you/the domain owner can make (I will not proceed on these alone)

1. **Signature re-hash (SIG-1, SIG-4, SIG-7, CMP-4).** Correcting the content-hash coverage and replacing the
   pseudo-version invalidates **every existing signature row** (all read "record changed"). This is an
   operational/legal decision — a coordinated re-sign campaign or a hash-formula-version column — not a code
   change I can make unilaterally. Needs your call on rollout.
2. **Complaint workflow policy (CMP-2, CMP-20, CMP-21).** The legal stage transitions (esp. reopen→re-
   investigate) and the disposition of invalid complaints are business rules the SRS defines; the auditor had
   INSUFFICIENT EVIDENCE (SRS not in the audit context). Confirm the intended machine before I encode it.
3. **Commission formula (CPN-10).** Whether commission is sample-count- or income-based, and whether
   `GoalType`/`GoalDuration`/`Metric` drive variant formulas, is unresolved — the engine ignores those fields
   today. INSUFFICIENT EVIDENCE; needs the reference platform's behavior or a product statement.
4. **Org-wide vs scoped commissions (CPN-2).** If commissions are deliberately org-wide, that is an accepted
   exception requiring an ADR (the standard forbids a buried code comment as the record); if not, they must be
   scope-filtered. Your choice determines the fix.
5. **Org-scope on identity lists (IDN gap).** Whether a scoped sub-admin may see all users/roles/sessions is
   undefined in the schema (identity carries no org dimensions). Product decision.

### Approval request

All five slices are audited; the Blocker set is complete. **Approve remediation of which findings?**

Recommended first wave — **Group 0 + Group 1** (BRD-1 and the five SCOPE-READ leaks): six of the eleven
Blockers, all self-contained, **no schema migration, no external API-contract change, no existing-test
rewrite**, each shipping the org-scope / rollover test the standard requires and F-201/F-202 flag as missing.
This is the highest security-and-stability value for the lowest blast radius, and it is safe for me to
execute one finding at a time under the Phase-4 protocol (test-first, gates re-run per finding, one commit
each) without any of the escalations below.

The remaining Blockers need a decision before I touch them:
- **Group 2** (CMP-2, SIG-2) needs the complaint-workflow / signing-privilege **policy** (escalations 2 & the
  SIG-2 privilege-to-meaning mapping); **CPN-3** is executable now.
- **Group 3** (IDN-1, IDN-2) is executable now (IDN-1 is behavioural, not schema).
- **Group 4** (SIG-1 + the e-sign cluster) is blocked on the **re-sign rollout decision** (escalation 1).

Tell me any of: "Group 0+1", "all executable Blockers (adds CPN-3, IDN-1, IDN-2)", a specific ID list, or a
decision on any escalation (1–5) to unblock its group. I will not modify a file until you choose.

---

## Phase 4 — Remediation log (cycle 2, Group 0 + Group 1 — approved 2026-08-27)

Branch: `remediation/cycle2-group0-1` (off `main`). Six findings fixed test-first; each fix proven to fail
without it (red) and pass with it (green), then the full IntegrationTests + ArchitectureTests re-run for
regression. **No database migration, no external API/OpenAPI change, no existing-test rewrite** — as scoped.
Test DB: throwaway `followup_remediation` on the dev cluster (dropped after). **Not yet committed** — see the
git-state note below.

| ID | Fix (minimal, reuses existing abstractions) | Files | Red proof | Green |
|---|---|---|---|---|
| BRD-1 | `RollToMonthlyAsync` resolves `MonthlySamples.Local` before the DB (mirrors `SampleTrackingRepository.GetByAreaDateAsync`) so several rolling visits/lab/day roll into one row | `BoardService.cs`; test `JobsTests.Rollover_rolls_multiple_verified_visits…` | reverted fix → `Npgsql 23505: duplicate key … ix_monthly_sample_laboratory_id_period` | pass |
| CMP-1 | `IComplaintQueries.GetByIdAsync`/`GetAuditAsync` take `OrgScope`; by-id joins `Laboratories.ApplyScope(scope)` (→404), audit gated by a scoped existence check (→empty); handlers pass `_user.Scope` | `ComplaintContracts.cs`, `ComplaintMarketingQueries.cs`, `ComplaintQueries.cs` | neutered `.ApplyScope` → Giza caller reads a Cairo complaint + audit | pass |
| CPN-1 | `ICompensationQueries.GetLabLedgerAsync` takes `OrgScope`; filters on scoped labs (mirrors `GetLedgersAsync`); handler passes `_user.Scope` | `Compensation.cs`, `StatsCompensationQueries.cs` | neutered scope filter → Giza caller reads a Cairo lab's ledger | pass |
| BRD-5 | `IDailyBoardQueries.GetSuggestedSampleCountAsync` takes `OrgScope`; the visit lookup joins scoped labs; handler passes `_user.Scope` | `DailyBoardContracts.cs`, `OperationsQueries.cs`, `GetDailyBoard.cs` | neutered scope join → Giza caller reads a Cairo lab's suggested count | pass |
| SIG-3 | `VerifySignatureHandler` resolves the record's lab and enforces scope via the canonical `ComplaintActionSupport.LoadAuthorizedAsync` (single-sourced) before disclosing signature metadata; unknown modules fail closed | `ElectronicSignatures.cs` | neutered scope call → Giza caller verifies a Cairo complaint's signature (no `ForbiddenException`) | pass |
| CPN-2 | `GetCommissionsAsync` now **applies** its previously-dead `OrgScope` parameter — reps filtered by Branch/Governorate/City/Area via `OrgScope.Allows` (lab-only Category/Segment wildcarded); unattributed reps visible only to a global caller (fail-closed). Decision (2026-08-27): **scope-filter**, not org-wide — so no ADR exception; escalation #4 resolved | `StatsCompensationQueries.cs` | neutered scope filter → Giza caller sees a Cairo rep's commission/salary row | pass |

Test additions: `tests/FollowUp.IntegrationTests/ScopeReadIsolationTests.cs` (4 isolation tests) and one
rollover regression in `JobsTests.cs`.

Gates (Debug, dev cluster): **IntegrationTests 23/23** (was 18; +1 BRD-1 +4 scope), **ArchitectureTests
21/21** (run in the isolated clone — the working-tree Api process holds a copy-lock on Api-referencing
builds; a rule-level re-verification confirmed `VerifySignatureHandler`'s added repositories stay covered by
the existing `ReviewedQueryHandlerRepositoryUse` allowlist, so no new `Query_handlers_do_not_touch_the_write_side`
violation). No regression.

### Group 3 (IDN-1, IDN-2) — approved & committed 2026-08-27

| ID | Fix | Files | Red proof | Green |
|---|---|---|---|---|
| IDN-1 | Account lockout was inert (bad-password increment rolled back with the command transaction). New `IFailedLoginRecorder` persists the attempt in a fresh DI scope (separate DbContext/connection) that survives the rollback, reusing `AppUser.RegisterFailedLogin` | `IFailedLoginRecorder.cs` (new), `FailedLoginRecorder.cs` (new), `Auth.cs`, `DependencyInjection.cs` | neutered the recorder call → 10 bad logins leave persisted `FailedLoginCount` at 0, account never locks | pass |
| IDN-2 | A keyed login serialized its bearer token into `idempotency_record`. `LoginCommand` marked `IExcludeFromIdempotency`; `IdempotencyBehavior` skips caching such commands | `IExcludeFromIdempotency.cs` (new), `Auth.cs`, `IdempotencyBehavior.cs` | neutered the exclusion → a keyed login stores a `LoginCommand` idempotency row (with the token) | pass |

Tests: `AuthSecurityTests` (2 cases). Gates: Domain 51/51, Application 58/58, Integration 26/26, Architecture 21/21.

**Process correction (honest reporting):** the Group-1 commit `54eab1e` shipped a **compile break** — SIG-3 added
record-scope dependencies to `VerifySignatureHandler`'s constructor, but the unit test `AuthAndSignatureTests`
that news it up directly was not updated, and Application.Tests was not re-run after SIG-3 (only Integration +
Architecture were). Caught while building Application.Tests for Group 3. Fixed here: the test now constructs the
handler with the new deps and a real in-scope complaint. **Lesson: run every affected test project, not just the
gate suites, per finding** — recorded in memory.

### Group 2 (CMP-2, SIG-2) — approved & committed 2026-08-27

Both "escalations" were resolved by reading the SRS (`01-srs.html` FR-11/FR-19, `02-workflows.html` §7) — which
IS in the repo; the audit subagents didn't have it. No business rule was invented.

| ID | Fix | Files | Red proof | Green |
|---|---|---|---|---|
| CMP-2 | `MoveToStage` refuses the gated terminal stages (Resolution via Resolve, RejectedInvalid via CheckValidity) and every stage mutation refuses a Resolved complaint — closing the resolve/e-signature bypass and post-resolution edits. Per FR-11 the stage stays a captured narrative (only status is a 409 machine), so legal in-flight moves are unaffected | `Complaint.cs`, `ComplaintTests.cs` | without guards: `MoveToStage(Resolution)` succeeds and a resolved complaint's narrative is editable | pass |
| SIG-2 | `SignRecordHandler` enforces org-scope after re-auth (FR-19: sign is "within organizational scope"), reusing the shared `SignatureRecordScope` helper extracted from the verify path (SIG-3) | `SignatureRecordScope.cs` (new), `ElectronicSignatures.cs`, `AuthAndSignatureTests.cs` | without the check: a Giza-scoped signer signs a Cairo complaint | pass |

FR-19 specifies **re-auth + scope only** — no privilege-to-meaning mapping (my earlier escalation was
over-cautious). Domain 53/53, Application 59/59, Integration 26/26, Architecture 21/21.

**Committed (2026-08-27, per user instruction "commit only the cycle-2 fixes"):** five commits on the branch —
`49cdde9` Group 0 (BRD-1), `54eab1e` Group 1 (CMP-1, CPN-1, CPN-2, BRD-5, SIG-3), `255fa21` Group 3
(IDN-1, IDN-2 + the SIG-3 unit-test correction), `796c79e` CMP-2, `2dcfc25` SIG-2.

### CPN-3 and SIG-1 — committed 2026-08-27

| ID | Fix | Files | Red proof | Green |
|---|---|---|---|---|
| CPN-3 | `SaveCommissionHandler` enforces org-scope via a new `ScopeGuard.EnsureInScope(Representative)` overload (reps: Branch/Governorate/City/Area; Category/Segment wildcarded — mirrors CPN-2) | `ScopeGuard.cs`, `Compensation.cs`, `SaveCommissionHandlerTests.cs` | neutered → a Giza caller saves a Cairo rep's payroll | pass |
| SIG-1 (+ SIG-7) | `RecordHasher` now hashes **all** material fields as JSON (unambiguous). **Hard cutover** (user decision 2026-08-27, `docs/adr/0008`): pre-existing signatures invalidate → re-sign. SIG-4 (real monotonic version via a `Complaint` xmin migration) deferred as a separate migration-gated Major | `ElectronicSignatureServices.cs`, `AuthSecurityTests.cs`, `docs/adr/0008-esign-hash-hard-cutover.md` (new) | with the old `\|`-joined hash: changing the resolution summary leaves the digest unchanged | pass |

Commits: `09ddad3` (CPN-3), `e716f64` (SIG-1). Full suite at HEAD: Domain 53/53, Application 60/60,
Integration 27/27, Architecture 21/21. (ApiTests unaffected — none of Group 2/CPN-3/SIG-1 touch the
login/lab/idempotency endpoints it covers; last verified 9/9 at the Group-3 close-out.)

**Blocker status: ALL 11 of 11 fixed.**

### Concurrency-token batch (IDN-4, CPN-9, CMP-6, BRD-2 + SIG-8) — committed `7a701e8`, 2026-08-28

Added `IVersioned` (Postgres `xmin`) to **Role, AppUser** (IDN-4), **CompensationConfig** (CPN-9),
**Complaint** (CMP-6 — also covers SIG-8's resolve TOCTOU), **DailyVisit** (BRD-2). These were last-writer-wins;
now a stale concurrent update conflicts (409). Non-destructive: `xmin` is a system column, so the migration is
an Npgsql no-op (verified — applies from zero, zero real columns, drift-check empty). Behavioural test
`Concurrent_updates_to_a_newly_versioned_aggregate_are_rejected`; the architecture token-rule now covers all 7.
Gotcha recorded in memory: the bare `IsRowVersion()` is nondeterministic for xmin — use the explicit
`.HasColumnName("xmin").HasColumnType("xid")` form. Follow-up (separate Major): expose `RowVersion` in the
update-command DTOs for the client-passes-version flow.

Remaining work: **~34 Majors** (IDN-5 session revoke, BRD-3 rollover transaction, SIG-4 real signature version,
F-201 test-race, F-202 authz-matrix, the RowVersion-in-DTO follow-up, and the duplication/DB-constraint cluster),
the **32 Minors / 9 Opinions**, and the still-uncommitted **cycle-1 work + audit docs**. To keep them pure, the
four shared files that also carried cycle-1 `dotnet format` reflow (ComplaintQueries, ComplaintMarketingQueries,
OperationsQueries, JobsTests) were reset to HEAD and re-applied with only the cycle-2 logic, then re-tested
(24/24). The cycle-1 remediation (vuln pins, ProblemDetails 400 mapping, repo-wide format, the three arch-test
files, the encrypted-lab test fix) and this cycle's audit docs remain **uncommitted** in the working tree, as
instructed. Not pushed.

