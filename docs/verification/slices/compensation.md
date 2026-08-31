# Slice register — Compensation (loyalty + commissions) — audit cycle 2, 2026-08-27

Adversarial read-only audit. Rule quotes cite `architect-standard.txt` line numbers. Org-scope = tenancy analog (ADR-0002).
Cross-refs honored: M-1 (validators for RecalculateLoyalty/RecalculateAllLoyalty/SaveCommission/SetCompensationConfig pinned); M-4 (UTC at StatsCompensationQueries.cs:102 — no *further* UTC business-date uses in this slice; RecalculateAll uses `_clock.CairoToday`); M-3 (no job touches this slice — loyalty recalc is manual-only).

---

ID: CPN-1 · **Blocker** · VERIFIED
Rule: "Enforce tenant isolation across: … Queries" (255,257); "Include automated tests proving that users cannot access another tenant's records." (269)
Location: StatsCompensationQueries.cs:114-119 (`GetLabLedgerAsync`); Compensation.cs:265-276 (`GetLoyaltyLedgerHandler`); AnalyticsEndpoints.cs:20-21
Evidence: `GetLabLedgerAsync(Guid labId, CancellationToken ct)` — filters only `x.LaboratoryId == labId`; no `OrgScope` parameter, no `EnsureInScope`, no `ApplyScope`. Sibling `GetLoyaltySummaryAsync` (:104) *does* scope.
Mechanism: SCOPE-READ defect reproduced. `GET /loyalty/ledger/{labId}` returns any lab's full loyalty history to any `ManageLoyalty` holder in any scope.
Impact: A governorate-scoped supervisor enumerates lab GUIDs and reads target/achieved/points/tier history of every lab in the network.
Fix: add `OrgScope scope` to `ICompensationQueries.GetLabLedgerAsync`, join `_db.Laboratories.ApplyScope(scope)` on LaboratoryId (mirror `GetLedgersAsync` :93-95), pass `_user.Scope` from the handler; or load lab + `EnsureInScope`. Files: Compensation.cs, StatsCompensationQueries.cs, + scope test.
Risk: cross-area viewers must hold wildcard scope; ledger DTO carries no code/name so ENC unaffected.

---

ID: CPN-2 · **Blocker** · VERIFIED
Rule: "Enforce tenant isolation across: … Queries" (255,257); "Record unavoidable exceptions as an ADR." (533)
Location: StatsCompensationQueries.cs:121-136 (`GetCommissionsAsync`); Representative.cs:47-51
Evidence: `GetCommissionsAsync(int period, OrgScope scope, CancellationToken ct)` — the `scope` parameter is accepted and **never used**; `_db.Representatives.Where(r => r.IsActive)` returns all. Representative carries Branch/Governorate/City/Area scope dimensions.
Mechanism: every active rep's BaseSalary/commission/bonus/total returns to any `ManageCommissions` holder regardless of scope. The in-code claim "documented as org-wide" is backed by nothing — no ADR/ASSUMPTIONS entry found; the dead `scope` parameter proves scoping was intended and dropped.
Impact: rep salary data (sensitive personal/compensation info) leaks across scopes; a branch-scoped payroll clerk sees every branch's payroll.
Fix: (a) filter reps by caller scope dimensions (wildcard-aware; new rep overload in ScopeFilter or inline predicate); or (b) write `docs/adr/0008-org-wide-compensation-reads.md` recording the exception + salary-exposure risk + revisit criteria, and remove the dead parameter. Files: StatsCompensationQueries.cs, Compensation.cs, optionally ScopeFilter.cs, docs/adr.
Risk: reps with NULL org attribution vanish for non-wildcard callers — decide null-handling; scoped totals change.

---

ID: CPN-3 · **Blocker** · VERIFIED
Rule: "Resource-level access checks" (333); "Resource-level authorization" (277)
Location: Compensation.cs:189-211 (`SaveCommissionHandler`)
Evidence: loads rep + config, computes, saves — **no scope/ownership check** after the load. Contrast the same file's `_user.EnsureInScope(lab)` (SetLabTargetHandler:60, RecalculateLoyaltyHandler:92).
Mechanism: privilege gate (`ManageCommissions`) exists but record-level authz absent; `ScopeGuard` has no `Representative` overload. Any `ManageCommissions` holder can create/recompute any rep's commission for any period.
Impact: out-of-scope payroll rows created/refreshed by unauthorized branches (state change + audit noise + materialized payout rows). With CPN-2, the whole commission surface ignores scope.
Fix: add `ScopeGuard.EnsureInScope(ICurrentUser, Representative)` (Branch/Governorate/City/Area vs scope, wildcard Categories/Segments), call in SaveCommissionHandler after load. Files: ScopeGuard.cs, Compensation.cs, + authz test.
Risk: reps lacking attribution fail closed for non-wildcard callers — define NULL semantics or backfill first.

---

ID: CPN-4 · Major · VERIFIED
Rule: "Do not create: … Duplicate business logic" (48,57)
Location: StatsCompensationQueries.cs:104-110 vs LaboratoryQueries.cs:54
Evidence: LaboratoryQueries `DisplayCode.For(l.Code.Value, canSeeEncrypted || !l.IsEncrypted)` vs StatsCompensation `DisplayCode.For(l.Code.Value, canSeeEncrypted)` — `IsEncrypted` not even projected.
Mechanism: per-lab confidentiality rule re-implemented per projection site; the loyalty copy omits `|| !l.IsEncrypted`.
Impact: for callers without `ShowEncryptedLabs`, the loyalty page shows the ENC alias for **every** lab (codes users search by are wrong network-wide). No leak, but the rule forked.
Fix: change `DisplayCode.For(realCode, isEncrypted, canSeeEncrypted)` so the rule can't be half-copied; update both call sites. Files: StatsCompensationQueries.cs, LaboratoryQueries.cs.
Risk: signature change touches every DisplayCode caller.

---

ID: CPN-5 · Major · VERIFIED
Rule: "Aggregate methods must protect invariants. Avoid public setters…" (181)
Location: CompensationConfig.cs:41-47 (private ctor), 65-71 (`Create`)
Evidence: private ctor assigns `CommissionRatePercent`/`BonusThresholdPercent`/`BonusAmount` with no validation; `Create` routes around `SetCommission` (which does reject negatives); `Money` accepts negatives.
Mechanism: first-time config (`existing is null` branch, Compensation.cs:235-239) constructs via `Create` → negative rate persists; validator absent (M-1).
Impact: negative rate persists; next `SaveCommission` → `Recompute` throws `DomainException` (RepCommission.cs:50-51) → commissions hard-fail for every rep until config repaired.
Fix: apply `SetCommission` guards + `bonusAmount < Money.Zero` in ctor/`Create` (ideally `Create` calls `SetCommission`). Files: CompensationConfig.cs + domain test.

---

ID: CPN-6 · Major · VERIFIED
Rule: "Aggregate methods must protect invariants. Avoid … uncontrolled collection mutation." (181)
Location: CompensationConfig.cs:73-77 (`SetTiers`), 88-92 (`TierFor`)
Evidence: `SetTiers` does `_loyaltyTiers.Clear(); _loyaltyTiers.AddRange(tiers);` — no checks. Empty set, duplicate names, duplicate `MinAchievementPercent` all accepted.
Mechanism: `Tiers: []` (validator absent — M-1) zeroes points/nulls tiers for every lab on next recalc; duplicate thresholds make `TierFor` order-dependent/nondeterministic.
Impact: silent loyalty-formula corruption; no error raised.
Fix: `SetTiers` requires ≥1 tier, unique names (case-insensitive), unique thresholds. Files: CompensationConfig.cs + domain test. (EF jsonb converter bypasses SetTiers, so existing bad rows load but can't re-save unchanged.)

---

ID: CPN-7 · Major · VERIFIED (mechanism; latent)
Rule: "Avoid: … Duplicate calculated data without a consistency strategy" (239,245)
Location: Compensation.cs:96-108 (`RecalculateLoyaltyHandler`); Laboratory.cs:71-74,160-167 (loyalty snapshot)
Evidence: `var period = YearMonth.FromCode(request.Period)` (arbitrary caller month) then `lab.SetLoyalty(...)` unconditionally overwrites the lab's current snapshot.
Mechanism: recalculating 2026-03 in August replaces the live loyalty page's tier/points with March's numbers while MtdSamples stays current-month. Consistency holds only when period == current month.
Impact: **latent** — grep confirms `RecalculateLoyaltyCommand` is dispatched from no endpoint/job. The moment a per-lab recalc route is mapped, historical recalcs corrupt current standing. `RecalculateAllLoyaltyHandler` (always current Cairo month, :143) is safe.
Fix: call `lab.SetLoyalty` only when `period == YearMonth.From(_clock.CairoToday)`; or remove the command (CPN-12). Files: Compensation.cs.

---

ID: CPN-8 · Major · VERIFIED
Rule: "Angular components must not contain business rules or direct persistence orchestration." (367)
Location: web/src/app/features/commissions/commissions.component.ts:91-98
Evidence: `save()` does `forkJoin(rows.map(r => this.api.post('/commissions/save', {representativeId:r.repId, period})))` — one command per rep, batch membership from last load, silent abort on error.
Mechanism: "save this month's payouts" exists only as client orchestration (N transactions, no rollback, no per-row feedback).
Impact: partial payroll months in production with no indication; N round-trips; undefined retry.
Fix: `SaveAllCommissionsCommand(int Period)` (ManageCommissions, per-rep scope per CPN-3, one transaction), `POST /commissions/save-all`, one client call. Files: Compensation.cs, AnalyticsEndpoints.cs, commissions.component.ts, handler test.

---

ID: CPN-9 · Major · VERIFIED
Rule: "Use: … Optimistic concurrency for conflicting updates" (209,211)
Location: CompensationConfig.cs:35 (not IVersioned); CompensationConfigurations.cs:57-85 (no token)
Evidence: `class CompensationConfig : AggregateRoot<string>, IAuditable` — no IVersioned/xmin. `SetCompensationConfigHandler` (:231-246) does read-modify-write on the singleton.
Mechanism: two admins editing config concurrently → later SaveChanges silently overwrites (last-write-wins); TransactionBehavior's 409 mapping can't fire; GET/POST carry no version/ETag.
Impact: silent loss of a money-formula change; all subsequent recalcs use the surviving (possibly wrong) formula.
Fix: `IVersioned`→xmin on CompensationConfig; optionally expose version in DTO + accept in command. Files: CompensationConfig.cs, CompensationConfigurations.cs, migration, Compensation.cs.

---

ID: CPN-10 · Major · **INFERRED**
Rule: "Do not invent critical business rules." (158)
Location: CompensationCalculator.cs:28-34; Compensation.cs:196-201; Representative.cs:39-43
Evidence: `achieved` = SUM(sample_count) (a count); `target` = rep.Target.Amount (Money/currency); commission = rate% × sample count. `GoalDuration`/`GoalType`/`Metric` on Representative are consumed nowhere in the engine (`GoalType` only display text).
Mechanism: one hard-coded formula for every rep type; units mixed (count vs currency); goal-variant fields imply variant formulas exist.
Impact: if any rep's goal is money-based or quarterly, attainment% and commission compute on the wrong basis — plausible systematic payroll misstatement.
INSUFFICIENT EVIDENCE: intended metric per GoalType/Metric/GoalDuration — need reference platform (:5080) or product-owner statement.
Fix (after confirming): branch engine on Metric/GoalType (samples vs income via DailyLabStatistic.Income), honor GoalDuration; or delete/document the unused fields. Files: CompensationCalculator.cs, Compensation.cs, CompensationData.cs, tests.
Risk: changes historical payout recomputation — decide retroactivity.

---

ID: CPN-11 · Major · VERIFIED — slice test coverage is 3 files (CompensationCalculatorTests, CompensationTests, SaveCommissionHandlerTests-happy-path). Zero tests for RecalculateLoyalty/RecalculateAll/SetLabTarget/SetCompensationConfig handlers, any query, any route, or scope isolation. The two Blocker leaks (CPN-1/2) are exactly the mandated tests that are absent. Fix: scope-isolation tests, RecalculateAll test, API contract tests. (269, 435, 439, 442, 444)

ID: CPN-12 · Minor · VERIFIED — dead/misleading code: `GetLedgersAsync` (correctly scoped) dispatched by nobody while unscoped `GetLabLedgerAsync` is live (CPN-1); `RecalculateLoyaltyCommand` unreachable (latent CPN-7); `CommissionDto.IsLocked` hardcoded `false` while UI ships a `lock_save_payouts` button. Fix: delete or wire; drop/implement IsLocked. (474)

ID: CPN-13 · Minor · VERIFIED — no CHECK constraints on lab_loyalty_ledger/rep_commission/compensation_config (points/target/base_salary/rate ≥ 0, non-empty tiers); SchemaHardening has zero compensation matches. Fix: one migration adding CHECKs. (247)

ID: CPN-14 · Minor · VERIFIED — get-then-add on (lab|rep, period) guarded only by unique index; concurrent first-time recalc/save → Postgres 23505 as `DbUpdateException` → unmapped → 500. Fix: map 23505→ConflictException in TransactionBehavior, or upsert. (324,335)

ID: CPN-15 · Minor · VERIFIED — 8 compensation routes declare no `.Produces`/`.ProducesProblem`; `/loyalty/recalculate` returns anonymous object; `/setup/compensation-config` binds the command directly; list endpoints return unbounded arrays. Fix: response DTOs + body records + annotations. (118,324,337,338,74)

ID: CPN-16 · Minor · VERIFIED — `loyalty.component.ts:76` uses `window.prompt` for target entry (untranslated, not a typed form); both components swallow every subscribe error (403/409/400/500 invisible). Fix: typed reactive form + ProblemDetails toast + success feedback. (361,368-375)

ID: CPN-17 · Minor · VERIFIED — `CommissionRatePercent`/`BonusThresholdPercent`/`MinAchievementPercent` are raw decimals with no upper bound (`rate = 5000` accepted end-to-end; validator absent M-1). "Percentage" is on the standard's VO list. Fix: `Percentage` value object (non-negative, bounded); interim = bounds in SetCommission/ctor. (182,188)

ID: CPN-18 · Opinion · VERIFIED — `RecalculateAllLoyaltyHandler` drives a write path from the read-model projection (`GetLoyaltySummaryAsync`) then does 3 round-trips × N labs in one transaction. Fix: purpose-built batch data method. (no rule — read/write-shape opinion)

---

## DUPLICATION
1. Encrypted-code masking rule diverged (LaboratoryQueries.cs:54 vs StatsCompensationQueries.cs:110) — **the dangerous kind**. Survivor: single `DisplayCode.For(realCode, isEncrypted, canSeeEncrypted)`; LaboratoryQueries semantics correct (CPN-4).
2. Loyalty standing stored twice (Laboratory snapshot vs lab_loyalty_ledger). Ledger is system of record; snapshot is a same-transaction current-period projection (violable via CPN-7).
3. "Achieved samples for a period" computed twice (`GetLabAchievedSamplesAsync` write path vs MTD grouping in `GetLoyaltySummaryAsync` read path) — legit CQRS split but drift-prone. Survivor: shared tested SQL shape, or read side reuses `ICompensationData`.
4. Achievement-percent math three times (calculator 2dp ToEven vs `loyalty.ach()`/`commissions.pct()` Math.round int) — display-only; server value survives; consider returning server percent in DTOs.
5. Two ledger reads, wrong one live (scoped `GetLedgersAsync` dead vs unscoped `GetLabLedgerAsync` live). Survivor: one scoped per-lab read (CPN-1+CPN-12).

## COVERAGE GAPS
Org-scope isolation violated on ledger read (CPN-1), commissions read (CPN-2), commission save (CPN-3); zero isolation tests (CPN-11). RecalculateAll trigger: any `ManageLoyalty` holder (ANY-of), writes scope-constrained; idempotency deterministic but header-gated behavior unused by client (client sends no key); first-insert race → 500 (CPN-14); not scheduled (manual by SRS design). Validators: 4 of 5 commands pinned (M-1); domain doesn't compensate on Create/SetTiers (CPN-5/6); percentages unbounded (CPN-17). Persistence: jsonb tier round-trip untested; no CHECKs (CPN-13). Contract: no API tests; anonymous response; dead IsLocked. Auditing: **covered** (AuditAndOutboxInterceptor writes AuditEntry per aggregate change).

## DEFINITION OF DONE (lines 500-519)
Acceptance **Not met** (FR-12(b) "scopes per rep" unmet CPN-3; SCOPE-READ unresolved CPN-1) · Domain behavior Partial (invariant holes CPN-5/6) · Architecture boundaries Met · CQRS Met (caveat: write handler consumes read model CPN-18) · Validation **Not met** (M-1) · Backend authz Partial (record-level gap CPN-3) · Tenant isolation **Not met** (CPN-1/2) · DB constraints Partial (no CHECKs CPN-13) · Indexes Met (ix on (lab,period)/(rep,period)) · Concurrency **Not met** (CPN-9/14) · Idempotency Partial (deterministic; header unused) · Auditing Met · Structured logging Met (INFERRED, inventory) · Distributed tracing INSUFFICIENT EVIDENCE (OTel wiring in Program.cs 1-99 unread) · Standard error handling Met (gap CPN-14) · Unit tests Partial (4/5 handlers untested) · Integration tests **Not met** · Security tests **Not met** (CPN-11) · Documentation Partial (org-wide commissions decision is a code comment, no ADR CPN-2) · Deployment/config Met.

## OBSERVED OUTSIDE SCOPE
- `Representative.UpdateProfile` (Representative.cs:78-87) omits the non-negative salary/target guard that `Register` (:72-73) enforces — negative salary persistable, detonates in RepCommission.Recompute. (Representatives slice)
- `LabStatsQueries.ListAsync` (StatsCompensationQueries.cs:30-32) loads all labs unscoped into memory for enrichment (no output leak; unbounded join) and re-implements wildcard-scope locally (`IsGlobal` :41-44). (LabStats slice)
- `SetLabTargetCommand` updates lab snapshot target but not the current ledger row's Target (refreshes on next recalc) — noted for the Lab/Compensation seam.

## VERIFIED vs ASSUMED
VERIFIED: all 4 Domain/Compensation files; Money/YearMonth; Representative/Laboratory loyalty members; Compensation.cs entire; ICompensationRepositories/SupportingRepositories(compensation)/CompensationData; StatsCompensationQueries entire; CompensationConfigurations; AnalyticsEndpoints; Program.cs:100-170; AuthorizationBehavior; ScopeGuard/ScopeFilter; AuditAndOutboxInterceptor; ExceptionHandlingMiddleware:61-71; migration snapshots; CqrsConventionTests:33-89; all 3 test files; both Angular components; SRS FR-12/BR-6/BR-9/SCOPE-READ extracts.
ASSUMED (inventory): TransactionBehavior one-tx + 409 mapping; IdempotencyBehavior header gating; LoggingBehavior; IVersioned→xmin convention; Money/YearMonth converters.
INFERRED: CPN-10 business impact (fields-ignored verified; intended formula variants not evidenced anywhere).
