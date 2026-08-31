# Slice register — Daily Board & Visits — audit cycle 2, 2026-08-27

Adversarial read-only audit. Rule quotes cite `architect-standard.txt` line numbers. Background Processing rules = lines 380-392. Cross-refs: M-3 (jobs bypass MediatR — ADR-0004), ST-1/ST-4/ST-6, M-1 referenced not re-derived.

---

ID: BRD-1 · **Blocker** · VERIFIED
Rule: "Prevent duplicate processing" (389); "Be retry-safe" (385)
Location: BoardService.cs:141-151 (`RollToMonthlyAsync`) + StatisticsConfigurations.cs:21 (unique index)
Evidence: `RollToMonthlyAsync` does `_db.MonthlySamples.FirstOrDefaultAsync(m => m.LaboratoryId == visit.LaboratoryId && m.Period == period, ct)` — a **database** query, not `.Local` — then `Add`s when null. Called in `foreach (var visit in toArchive)` (RunRolloverAsync:56-61) for every rolling visit. `monthly_sample` has `HasIndex(x => new { x.LaboratoryId, x.Period }).IsUnique()`.
Mechanism: a lab with two verified+received visits in a day (normal — multi-slot VisitTimes; the integration test itself creates two) yields two calls for the same `(lab, period)`. The lookup hits the DB, not the change tracker, so the second call doesn't see the row the first added-but-not-yet-saved (`SaveChanges` runs once, later, inside `GenerateBoardAsync`) → adds a **second** MonthlySample → the flush violates the unique index.
Impact: `RunRolloverAsync` throws on the midnight commit. Deterministic → Hangfire retries fail identically → poison. That night: yesterday is never archived, verified samples never roll to monthly totals (which feed loyalty/commission), and **today's board is never generated** (its inserts are staged into the same failing SaveChanges) — the operational heart stops until manual intervention.
Fix: resolve against `.Local` first (mirror `SampleTrackingRepository.GetByAreaDateAsync`, OperationsRepositories.cs:44): `_db.MonthlySamples.Local.FirstOrDefault(...) ?? await _db.MonthlySamples.FirstOrDefaultAsync(...)`. Files: BoardService.cs + rollover integration test with two verified visits/lab/day.
Risk: none — local-cache lookup only sees rows added earlier in the same run (correct; one loop/one context). No schema change.

---

ID: BRD-2 · Major · VERIFIED
Rule: "Optimistic concurrency where required" (47); "Optimistic concurrency for conflicting updates" (211)
Location: DailyVisit.cs:44 (no IVersioned); OperationsConfigurations.cs:9-43 (no token)
Evidence: `class DailyVisit : AggregateRoot<DailyVisitId>, IAuditable` — not IVersioned; no RowVersion/xmin mapping.
Mechanism: DailyVisit is mutated by seven concurrent paths (CheckIn, Miss, Undo, Verify, ConfirmTransfer, ConfirmReceipt, + the 22:00 sweep). MissedSweepJob vs a collector's CheckIn, or Verify vs Undo, routinely hit the same Pending row. No token → EF issues no version predicate → TransactionBehavior's 409 mapping can never fire → last-writer-wins.
Impact: a concurrent sweep loads the pre-check-in snapshot and writes `Missed` over a just-recorded `Visited`, silently discarding the collector's SampleCount/Transfer and orphaning the auto-created outsource row. Data loss, no error surfaced.
Fix: `IVersioned` (uint RowVersion→xmin) on DailyVisit, map in DailyVisitConfiguration, migration; update DomainModelTests concurrency-token ratchet. Files: DailyVisit.cs, OperationsConfigurations.cs, migration, arch-test allowlist.
Risk: callers must handle 409 on visit actions; batch handlers 409 on a stale item and roll back (desired) — the Angular board must show the conflict and reload.

---

ID: BRD-3 · Major · VERIFIED
Rule: "Database transactions for aggregate consistency" (210); "Be retry-safe" (385)
Location: BoardService.cs:33-68 (three independent commits; archive selects `VisitDate == yesterday`)
Evidence: `RunMissedSweepAsync(yesterday, ct)` commits (own SaveChanges :39); then archive/roll staged; then `GenerateBoardAsync(today, ct)` flushes (:137); then a redundant `SaveChangesAsync` (:66). The archive query is `Where(v => v.VisitDate == yesterday)`.
Mechanism: no ambient transaction (bypasses TransactionBehavior — M-3). Yesterday's sweep commits separately before the archive/board commit. If the archive/board commit fails permanently, yesterday's rows stay un-archived; next night `yesterday` advances a day and those rows are never selectable again.
Impact: a single failed roll-over permanently orphans one day of visits — absent from visit_history, excluded from monthly_sample, yet still returned by live board/lifecycle queries (which read daily_visit by date range). Silent under-counting of commission inputs.
Fix: wrap the whole roll-over in one explicit transaction/execution-strategy (or fold the missed-sweep into the same unit, drop its inner SaveChanges); remove the redundant :66 save; consider `VisitDate <= yesterday` so stragglers eventually archive. Files: BoardService.cs.
Risk: one large transaction holds locks longer over midnight; `<=` changes the "exactly one day per run" invariant — adjust reports/tests.

---

ID: BRD-4 · Major · VERIFIED
Rule: "Duplicate business logic" (57)
Location: BoardService.cs:117-122 (`GenerateBoardAsync`) vs :86 (`ReconcileLabTodayAsync`) vs LaboratoryStatus.cs:40
Evidence: `GenerateBoardAsync` filters `Where(l => l.Status == active || l.Status == pending || l.Status == interactive)`; `ReconcileLabTodayAsync` uses `lab.Status.IsSchedulable`; domain `IsSchedulable => this == Active || this == Pending || this == Interactive`.
Mechanism: the "which statuses go on the board" invariant is authoritative in `IsSchedulable` and used by reconcile, but re-encoded inline in generation. Two sources of truth.
Impact: adding a fourth schedulable status by editing `IsSchedulable` silently diverges — intra-day reconcile schedules it, midnight generation does not; labs appear on today's board then vanish next morning.
Fix: filter generation by the domain predicate (client-evaluate on `l.Status.IsSchedulable`, or expose a mapped status set). Files: BoardService.cs.
Risk: client-side evaluation loads all non-terminal labs before filtering — negligible volume; confirm the query still translates.

---

ID: BRD-5 · Major · VERIFIED — *(same org-scope-read-leak family as the Blockers CMP-1/CPN-1/CPN-2/SIG-3; kept Major only for the trivial sensitivity of a single sample count)*
Rule: "Enforce tenant isolation across: … Queries" (255-258)
Location: OperationsQueries.cs:75-85 (`GetSuggestedSampleCountAsync`); handler GetDailyBoard.cs:23-28
Evidence: loads the visit by id then `Where(v => v.LaboratoryId == visit.LaboratoryId && v.SampleCount != null)` with **no** `.ApplyScope`; handler forwards `request.VisitId` with no `EnsureInScope`. Every other read in the file threads `.ApplyScope(scope)`.
Mechanism: any `ViewDailyFollowup` holder passes an out-of-scope visitId and receives that lab's most-recent sample count.
Impact: cross-scope disclosure of a lab's operational data (practically GUID-gated).
Fix: load the visit's lab and `_user.EnsureInScope(lab)` (as CheckInVisitHandler does via VisitActionSupport), or `.ApplyScope` in the query; + tenant-isolation test. Files: GetDailyBoard.cs or OperationsQueries.cs.
Risk: an out-of-scope id returns 403 not a value — align the Angular popup to treat that as "no suggestion".

---

ID: BRD-6 · Major · VERIFIED (missing check); INFERRED (verify-only-collected rule)
Rule: "Aggregate methods must protect invariants. Avoid public setters" (181); "The frontend is never the security boundary." (294); "Angular components must not contain business rules" (367)
Location: DailyVisit.cs:157 (`SetVerified`); VerifyVisitHandler DailyBoardCommands.cs:162-167; gate only in daily.component.ts:91
Evidence: `public void SetVerified(bool verified) => AdminChecked = verified;` — naked setter, no state guard; handler `visit.SetVerified(request.Verified)` with no status check. The `@if ((v.status === 'Visited' || v.status === 'Received') && !v.adminChecked && auth.has('VerifyDailyFollowup'))` is the only gate.
Mechanism: `POST /daily/{id}/verify` accepts any visit id; SetVerified sets AdminChecked unconditionally. `RollsToMonthly = AdminChecked && Status == Received`, and nothing clears AdminChecked across transitions.
Impact: a direct API call verifies a Pending visit; when later checked-in→received it rolls to monthly totals though never verified in the collected state — corrupting the verified-sample count that drives commissions.
Fix: `SetVerified` requires `Status == Visited || Status == Received` (throw otherwise). Files: DailyVisit.cs + domain test.
Risk: breaks any legitimate verify-before-collection flow — confirm none exists.

---

ID: BRD-7 · Major · VERIFIED (missing check); INFERRED (collector-type rule)
Rule: "The frontend is never the security boundary." (294); "Aggregate methods must protect invariants. Avoid public setters" (181)
Location: DailyVisit.cs:115 (`ReassignCollector`); CheckInVisitHandler DailyBoardCommands.cs:58-90 (no reps repo); UI filter daily.component.ts:168
Evidence: `public void ReassignCollector(RepresentativeId? repId) => CollectorRepId = repId;` — no existence/type check; CheckInVisitHandler injects visits/labs/outsource/user/clock but **no** IRepresentativeRepository; `if (request.CollectorRepId is { } repId) visit.ReassignCollector(new RepresentativeId(repId))`. The "Collector/Scanning only" rule is an Angular `computed` filter.
Mechanism: ConfirmTransferHandler validates its rep via `_reps.ExistsAsync`; CheckInVisitHandler does not — assigns any GUID.
Impact: a crafted check-in assigns a non-existent rep (fails late at the Restrict FK as an unmapped DbUpdateException → 500) or a valid wrong-type rep (e.g. Marketing) as the credited collector — mis-attributing samples/commission.
Fix: inject IRepresentativeRepository into CheckInVisitHandler; verify existence + (if a rule) collector eligibility before ReassignCollector. Files: DailyBoardCommands.cs.
Risk: adds a repo round-trip to check-in; keep it on the optional-reassignment path only.

---

ID: BRD-8 · Major · INFERRED
Rule: "Explicit state transitions" (43); "Business rules and security enforcement must remain on the backend." (132)
Location: ConfirmReceiptHandler LabCheckIn.cs:71-84 (+ batch :115-132); read-side precondition only at OperationsQueries.cs:146-147
Evidence: `ReceiveAtLab` asserts only Visited→Received; the "must be transferred first" gate lives in the read projection `where v.TransferConfirmedAt != null && (v.Status == visited || v.Status == received)` (GetAwaitingReceiptAsync). The handler loads by id and calls `visit.ReceiveAtLab(_clock.UtcNow)` with no `TransferConfirmedAt` precondition.
Mechanism: the FR-6→FR-7 sequence (transfer → capture driver → receipt) is enforced only by the list projection; a Visited-not-transferred visit can be received directly via `POST /labcheckin/confirm`.
Impact: if transfer-before-receipt is an invariant, it's bypassable via the API, skipping driver/chain-of-custody capture. (INFERRED — the state machine deliberately models Visited→Received as legal; escalate to Blocker if the reference requires prior transfer.)
Fix: if required, guard in `ReceiveAtLab` (throw unless `TransferConfirmedAt is not null`). Files: DailyVisit.cs + test.
Risk: breaks any legitimate walk-in/no-transfer receipt path.

---

ID: BRD-9 · Major · VERIFIED (config); INFERRED (concurrent inserts occur)
Rule: "Use database constraints as a second line of defense for business integrity." (247)
Location: OperationsConfigurations.cs:40-42 (only non-unique indexes on daily_visit)
Evidence: `HasIndex(VisitDate)`, `HasIndex(LaboratoryId, VisitDate)`, `HasIndex(Status)` — no UNIQUE on `(LaboratoryId, VisitDate, ScheduledTime)`. Contrast outsource_sample (:95) and sample_tracking (:119) which `.IsUnique()`.
Mechanism: duplicate-visit prevention is app-level only — `GenerateBoardAsync` skips a lab if any row exists (:129), `ReconcileLabTodayAsync` skips present times (:105). These run in **different** Hangfire jobs (BoardRolloverJob vs NotificationDispatchJob→BoardSchedulingHandler); `[DisableConcurrentExecution]` guards same-job only. Both can read an empty set and insert the same `(lab, date, time)`.
Impact: duplicate board rows → double-counted samples at roll-over, duplicated transfer/receipt work items.
Fix: `HasIndex(x => new { x.LaboratoryId, x.VisitDate, x.ScheduledTime }).IsUnique()` + migration; handle the conflict gracefully in the generators. Files: OperationsConfigurations.cs, migration.
Risk: migration fails if a legitimate duplicate `(lab,date,time)` exists — pre-check before deploy.

---

ID: BRD-10 · Minor · VERIFIED (code); INFERRED (impact depends on schedule data) — the standalone 22:00 sweep marks **every** still-Pending visit Missed with no `ScheduledTime <= now` guard (BoardService.cs:33-42), so a visit scheduled after 22:00 is missed before its slot. Low impact if labs are daytime-only. Fix: add `&& v.ScheduledTime <= _clock.CairoNow.TimeOfDay` when `date` is today. (43)

ID: BRD-11 · Minor · VERIFIED — beyond ST-4's pinned `"Received"`, the read side adds more status string literals: `"Transferred"` (a display status not in VisitStatus, OperationsQueries.cs:182) and a `"Visited"` filter branch (:31-34), decoupled from the enumeration. Fix: centralize on `VisitStatus`/a display-status mapping (same pass as ST-4). (55)

ID: BRD-12 · Minor · VERIFIED — the BR-7 display-code branch policy (real vs ENC alias) is expressed twice: `Laboratory.DisplayCode` (Laboratory.cs:220-221) and `DisplayCode.For` (LaboratoryQueries.cs:12-16). The alias algorithm itself is single-source in `LabCode.ToEncryptedAlias` (good); only the branch policy duplicates. (Note: this is the same DisplayCode helper CPN-4 wants to fix — coordinate.) Fix: reuse the domain via a shared policy method, or document the read-projection helper. (57)

---

## DUPLICATION
- Schedulable-status rule (BRD-4): `LaboratoryStatus.IsSchedulable` (domain) vs inline in `GenerateBoardAsync`. Survivor: domain predicate.
- Display-code policy (BRD-12, overlaps CPN-4): `Laboratory.DisplayCode` vs `DisplayCode.For`; alias single-source in LabCode. Survivor: domain policy / one shared helper.
- Status literals vs VisitStatus (ST-4 + BRD-11): `"Received"`/`"Transferred"`/`"Visited"` across OperationsQueries.cs and daily.component.ts. Survivor: VisitStatus.
- Cairo timezone resolution (outside slice): identical `ResolveCairo()` in SystemClock.cs:14-24 and BackgroundJobsRegistration.cs:75-84. Survivor: SystemClock/IClock.
- Semantic: ConfirmReceiptHandler/ConfirmReceiptsBatchHandler (LabCheckIn.cs) repeat load-lab-EnsureInScope-ReceiveAtLab-DeriveActive; same for ConfirmTransfer/ConfirmTransfersBatch (Transfers.cs). Extract a shared support method (as VisitActionSupport already does for board actions).

## COVERAGE GAPS
- Concurrency (446): no conflicting-DailyVisit test (BRD-2, no token to test).
- Idempotency (445): JobsTests covers generate+sweep+reconcile+prune but **not** double-rollover, the BRD-1 two-verified-visits case, or roll-over re-run after partial failure.
- Org-scope isolation (269): no board/transfer/lab-check-in isolation test; BRD-5 leak untested.
- Authorization: Miss/Undo/Verify/ConfirmReceipt ship no validator (M-1); the verify-state (BRD-6) and collector-type (BRD-7) guards are untested because unenforced.
- API contract (442): ContractTests has no `/daily`, `/transfers`, `/labcheckin` route (grep: zero) — no contract coverage for the slice's 11 routes.
- Domain invariants: DailyVisitTests covers check-in/miss/undo/receive/transfer but not SetVerified constraints or receipt-requires-transfer (both unguarded).
- Persistence: no unique `(lab,date,time)` constraint (BRD-9); no duplicate-rejection test.

## DEFINITION OF DONE (lines 500-519)
Acceptance Partial (verify/receipt sequencing gaps BRD-6/8) · Domain behavior Partial (naked setters SetVerified/ReassignCollector) · Architecture boundaries **Gap** (roll-over orchestration in Infrastructure BoardService — M-3) · CQRS Met · Validation Partial (Miss/Undo/Verify/ConfirmReceipt unvalidated — M-1) · Backend authz Met mostly (exception BRD-5) · Tenant isolation Gap (BRD-5) · DB constraints Gap (BRD-9; daily_visit has no status CHECK) · Indexes Met · Concurrency **Gap** (BRD-2) · Idempotency **Gap** (BRD-1; missed-sweep itself idempotent) · Auditing Met · Structured logging Partial (BoardSchedulingHandler swallows-and-logs, poison silently dropped) · Distributed tracing INSUFFICIENT EVIDENCE (Program.cs OTel not opened; jobs bypass MediatR → no span) · Standard error handling Met (caveat BRD-7 late FK DbUpdateException) · Unit tests Partial · Integration tests Partial (no rollover/archive/monthly test — would have caught BRD-1) · Security tests **Gap** (no isolation/authz test) · Documentation Met · Deployment/config Met (no explicit `[AutomaticRetry]` — relies on Hangfire default 10).

## OBSERVED OUTSIDE SCOPE
- `BoardSchedulingHandler` (Application) catches **all** exceptions and never rethrows (:50-56) → a BRD-3 intra-day reconcile failure marks its outbox message processed and is lost — inconsistent with SampleReceiptTrackingHandler, which rethrows for outbox retry (ST-6 family). Worth an explicit ADR note.
- `OutboxDispatcher.ResolveType` (:64-68) reflects over all FollowUp.Domain* assemblies per message — perf smell, cross-slice.
- Angular appends unused `?source=daily` query params to POST URLs (daily.component.ts:232,247,249) — ignored by endpoints; harmless.
- `RecurringJobsInitializer` duplicates `ResolveCairo` from SystemClock (DRY, cross-slice).

## VERIFIED vs ASSUMED
VERIFIED (read): DailyVisit.cs, VisitStatus.cs, VisitHistory.cs, Events.cs, MonthlySample.cs, LaboratoryStatus.cs, LabCode.cs, Laboratory.cs:189-221, DailyBoardCommands.cs, GetDailyBoard.cs, DailyBoardContracts.cs, BoardSchedulingHandler.cs, LabCheckIn.cs, Transfers.cs, SampleReceiptTrackingHandler.cs, BoardService.cs, HangfireJobs.cs, BackgroundJobsRegistration.cs, OutboxDispatcher.cs, SystemClock.cs, OperationsQueries.cs, OperationsRepositories.cs, OperationsConfigurations.cs, StatisticsConfigurations.cs, ScopeFilter.cs, ScopeGuard.cs, AuthorizationBehavior.cs, OperationsEndpoints.cs, daily.component.ts, models.ts (BoardItem), snapshot :1098-1211/2331-2384, JobsTests.cs, DailyVisitTests.cs, BatchOperationsTests.cs, OperationalModulesTests.cs:1-120, BoardSchedulingHandlerTests.cs, CqrsConventionTests.cs:30-89, Enumeration.cs.
ASSUMED/INFERRED: multi-visit/day frequency (BRD-1 trigger; mechanism VERIFIED); the verify-only-collected / collector-type / receipt-requires-transfer business rules (BRD-6/7/8 — missing enforcement VERIFIED); rollover-vs-outbox concurrency (BRD-9). INSUFFICIENT EVIDENCE: OTel/Serilog span coverage for jobs (Program.cs not opened); Hangfire retry bound (inferred default 10).
