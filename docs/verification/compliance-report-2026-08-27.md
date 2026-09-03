# Architecture Conformance Report — Cycle 1 (2026-08-27)

Verification pack executed against `main` @ `791f567` (baseline tag `verify/baseline-20260827`).
All build/test evidence produced from a **fresh clone** (nothing reused from the working tree except the
fixes written back to it). Evidence artifacts: `docs/verification/baseline-20260827/` (repo) + session logs.

## Gate results

| Gate | Verdict | Evidence |
|---|---|---|
| 0 Baseline | **DONE** | Tag `verify/baseline-20260827`; `baseline-20260827/{openapi.json, jscpd-report.json, migrations-list.txt}` |
| 1 Reproducible build | **PASS** (2 deviations, below) | Fresh clone → `dotnet restore` + `dotnet build -c Release -warnaserror`: **0 warnings, 0 errors**. `tsc --noEmit` strict: clean. `ng build --configuration production`: success (1 CommonJS warning: `leaflet`). 15/15 migrations applied to an **empty** database in one pass, verified in `information_schema` (34 tables). Release boot → `/healthz/ready` **200** from cold seed |
| 2 Architecture tests | **PASS 21/21** | `dotnet test tests/FollowUp.ArchitectureTests -c Release` — 5 pre-existing dependency-direction tests + **16 new rules** added this cycle (see below) |
| 3 Duplication / dead abstraction | **PASS** | jscpd: **0.84% duplicated lines / 1.36% tokens** (threshold 3%), 10 clone pairs. `dotnet format --verify-no-changes`: **clean** (after fix). Semantic sweep: exactly one `IClock`, `ICurrentUser`, save-interceptor, ProblemDetails site; intentional null-object pairs documented |
| 4 Behavioural invariants | **PASS with gaps** | Full suite on fresh DB, **zero skips**: Domain **51/51**, Application **58/58**, Architecture **21/21**, Integration **18/18**, ApiTests **9/9**, Angular karma **7/7**. Live invariants proven: idempotency replay, optimistic-concurrency conflict, outbox drain, edge 401, login 429. Gaps listed below |
| 5 Contract & schema drift | **PASS** | `dotnet ef migrations add __DriftCheck` → **empty `Up()`** (removed). OpenAPI baseline captured: **99 paths / 126 operations** (first cycle — nothing to diff). Destructive-op scan: reviewed, no data-loss op (see below) |
| 6 Security | **PASS after fixes / 2 accepted exceptions** | 3 High NuGet transitives **fixed by pinning** (verified resolved: Newtonsoft.Json 13.0.1, System.Text.Json 8.0.5, Caching.Memory 8.0.1). History secret sweep (91 commits): no real leak. SPA bundle: no secrets. Rate limiter proven live (7×401 → 5×429). Open items below |
| 7 Slice review (Sample Tracking) | see addendum | Adversarial single-slice review of the most recently merged module |
| 8 Close-out | this report | Blockers: **0** · Majors open: **7** · Minors open: **12** (incl. slice) |

### Gate 1 deviations (environment, not product)
- This host's Docker daemon runs **Windows containers** — `postgres:17` (linux) cannot run. The empty-DB
  migration test used a throwaway database (`followup_verify_g1`) on the isolated dev cluster `:5442`
  instead of `docker compose up db`. Same evidence (migrations from zero), different transport.
- `docker compose up --build` (app image) not executed — the Dockerfile's linux base images cannot run
  here either. The Release boot test substituted for it. **The container build has still never been
  executed on any machine — CI is the right place for it.**

## Fixes applied this cycle (uncommitted, in the working tree — review with `git diff`)

| # | Change | Files |
|---|---|---|
| 1 | Pinned vulnerable High-severity transitives (GHSA-5crp-9r3c-p9vr, GHSA-8g4q-xg66-9fp4, GHSA-qj66-m88j-hmgj) via direct references | `Directory.Packages.props`, `src/FollowUp.Infrastructure/FollowUp.Infrastructure.csproj` |
| 2 | Malformed request bodies now map to **400** ProblemDetails (was 500 in Development, bare 400 in Production) and every problem response carries `application/problem+json` (was clobbered to `application/json`); `RouteHandlerOptions.ThrowOnBadRequest = true` so the mapping applies in every environment. Verified live: `{"type":".../400","title":"Malformed request",...}` | `src/FollowUp.Api/Middleware/ExceptionHandlingMiddleware.cs`, `src/FollowUp.Api/Program.cs` |
| 3 | Repo-wide `dotnet format whitespace` (21 files, verified content-identical ignoring whitespace) | `src/**`, `tests/**` |
| 4 | **16 new architecture tests** with ratchet allowlists (fail on new violations; pinned pre-existing gaps stay visible) | `tests/FollowUp.ArchitectureTests/{DomainModelTests,CqrsConventionTests,ApiAndInfrastructureRulesTests}.cs` |
| 5 | Fixed stale test `Encrypted_alias_is_applied_when_not_permitted`: masking is **per-lab** (`IsEncrypted` flag) since the reference-parity work — the test still asserted the old mask-everything semantics and has likely been red in CI since ~Aug 25. Now seeds a flagged lab and asserts both sides | `tests/FollowUp.IntegrationTests/ReadPathTests.cs` |
| 6 | Capability inventory created (Gate 3 prevention — attach to any prompt that requests a new module) | `docs/CAPABILITY-INVENTORY.md` |

New architecture rules: entities expose no public mutable setters ✓ · aggregate collections read-only ✓ ·
non-public EF constructors ✓ · value objects immutable ✓ · `IVersioned` aggregates map a concurrency token
(checked against the built EF model, offline) ✓ · EF model builds offline ✓ · handlers only in Application ✓
· every command has a validator (ratchet: 46 pinned) · every request declares privileges (ratchet: 2 pinned)
· query handlers don't touch the write side (3 reviewed exceptions) ✓ · no `IQueryable` across boundaries ✓
· no generic repository ✓ · endpoints free of Domain/Infrastructure (Health readiness ping excepted) ✓ ·
endpoints don't call repositories (ratchet: `/labs/nextcode` pinned) · hub has no client-invokable methods ✓
· jobs take no entities and own no `DbContext` ✓.

## Open findings

### Major (0 blockers — none of these is releasable-blocking alone, all need owners)

| ID | Finding | Location | Correction |
|---|---|---|---|
| M-1 | **46 of 75 commands have no validator** — includes free-text state inputs (`MoveComplaintStageCommand.Stage`, `AdvanceOutsourceStatusCommand.Status`, `UpsertSettingCommand`, `UploadLabImageCommand`) | pinned in `CqrsConventionTests.CommandsWithoutValidatorYet` | Write validators; remove each from the ratchet as it lands (test enforces removal) |
| M-2 | **`GetLaboratoriesQuery` / `GetLaboratoryByIdQuery` carry no `IAuthorizedRequest`** — any authenticated user can read the (scope-filtered) lab list regardless of privileges | `Features/Laboratories/GetLaboratories`, pinned in ratchet | Decide the required privilege (reference parity) and declare it; remove from ratchet |
| M-3 | **Background jobs bypass the MediatR pipeline, contradicting ADR-0004's own decision text** ("job resolves an application use case (MediatR command)… and does nothing else"). `BoardService`/`RetentionService` live in Infrastructure with EF LINQ + own `SaveChangesAsync` — no authorization context, no idempotency behavior, no transaction behavior, audit only via interceptor | `src/FollowUp.Infrastructure/Jobs/{BoardService,RetentionService,OracleSyncRunner}.cs` | Either move the logic into Application commands the jobs dispatch (conforms), or amend ADR-0004 to record the service-based design deliberately, with its tradeoffs |
| M-4 | **UTC used for business "today" in 5 read queries** (`DateOnly.FromDateTime(DateTime.UtcNow)`), e.g. loyalty summary month rolls 2–3 h late vs Cairo. Same defect class as the shipped bug reverted in `96a708b` | `Persistence/Queries/ComplaintMarketingQueries.cs:41`, `NotificationInsightsQueries.cs:170,223,271`, `StatsCompensationQueries.cs:102`, `AdminQueries.cs:124` | Inject `IClock` and use `CairoToday`/`CairoNow` |
| M-5 | **Angular 19.2 carries 7 High advisories** (compiler XSS ×2, `HttpTransferCache` poisoning ×2, `formatDate` DoS); no non-breaking fix — fix is Angular 22 (breaking; blocked by Node 20 per ADR-0007) | `web/package.json` | Accepted exception for now. Revisit criteria: upgrade host Node to 22 LTS → `ng update` through 20→22. Mitigation: app doesn't use SSR/TransferCache; XSS vectors need attacker-controlled templates/i18n — review usage of `[innerHTML]`/i18n attributes until upgraded |
| M-6 | **`POST /api/v1/signatures` verifies the user's password with no rate limit** (password oracle; login itself is limited) and `FOLLOWUP_ADMIN_PASSWORD` falls back to a literal (`Program.cs`) instead of failing fast like `FOLLOWUP_AUTH_SECRET` | `Endpoints` (signature route), `src/FollowUp.Api/Program.cs:74` | Apply `RequireRateLimiting("login")` (or a dedicated policy) to the signature endpoint; make the admin seed password required-from-env in Production (fail fast) |

### Minor

| ID | Finding | Correction |
|---|---|---|
| m-1 | `/labs/nextcode` injects `ILaboratoryRepository` into the endpoint, no privilege check (pinned in ratchet) | Move behind a query + declare privilege |
| m-2 | `GetRetentionHandler` / `GetIntegrationConfigHandler` read config via aggregate repositories (pinned as reviewed exceptions) | Move to `ISettingsQueries`-style projections |
| m-3 | `SignalRRealtimeNotifier.DataChangedAsync` broadcasts to `Clients.All` (entity-type labels reach every connected client, regardless of scope) | Scope data-changed hints per user group or drop payload detail |
| m-4 | `MapLinkResolver` injects `IHttpClientFactory` but news up `HttpClient` per call | Use a named client configured with `AllowAutoRedirect=false` |
| m-5 | `lab-create.component.ts` ↔ `lab-detail.component.ts` share ~114 duplicated lines (largest jscpd clones) | Extract the shared lab-form into one component/service |
| m-6 | `leaflet` CommonJS warning in production build | `allowedCommonJsDependencies` or ESM leaflet build |
| m-7 | No ESLint configured for `web/` (pack expects `eslint --max-warnings 0`) | Add `@angular-eslint` |
| m-8 | Hangfire dashboard auth is loopback-only with no forwarded-headers config (its doc comment already says production should privilege-gate it) | Replace with a `ManageUsers`-gated filter before any proxy deployment |

### Accepted exceptions (record/refresh as ADRs)
- **Angular 19 with known highs** — ADR-0007 (Node constraint) already records the version choice; append the advisory list + revisit criteria (M-5).
- **OpenTelemetry 1.9.0** (`OpenTelemetry.Api` Moderate GHSA-g94r-2vxg-569j): fix requires OTel ≥1.10 → Microsoft.Extensions 9.x wave, conflicting with the repo's 8.0.x pins. Defer to a deliberate platform upgrade; new ADR suggested.
- **Reviewed non-defects**: `HealthEndpoints` pings `FollowUpDbContext` (readiness); `VerifySignatureHandler` loads the aggregate to run `StillValidFor` (domain behavior); migration `LabRepReferenceParity` drops `collector_rep_id` **after** migrating its data to `collector_reps` (`Down()` restores); `InitialCreate` `DropTable`s are `Down()`-only; CI/compose dummy credentials are placeholders overridden by env.

## Gate 4 coverage & remaining test gaps

Per-assembly line coverage (max across suites — union not computable offline; `reportgenerator` needs the
registry, and coverlet under-reports Infrastructure with `Deterministic=true` builds — fix with
`coverlet.runsettings` + `DeterministicSourcePaths` next cycle):
`FollowUp.Api` **82.9%** · `FollowUp.Domain` **52.1%** · `FollowUp.Application` **27.4%** · `FollowUp.Infrastructure` unreliable (measurement artifact).

Missing test categories (the pack's §4 list):
1. **Authorization matrix** (endpoint/request × privilege): nothing close exists; the machine-readable
   surface is `IAuthorizedRequest.RequiredPrivileges` — a data-driven test over all requests × roles is
   feasible today and should also fail when a new request omits an expectation entry.
2. **Outbox dedup / poison-message / MaxAttempts exhaustion** (delivery is tested; redelivery-dedup only at
   one handler).
3. **Concurrency conflict** covered for `Laboratory` only — add `Representative`.
4. **Org-scope dimension matrix** — Governorate + Segment are proven in SQL; Branch/City/Area/Category are not.
5. **Playwright e2e**: exists (1 spec) but **hard-pins :5088**, which is the live dev instance on this host —
   running it would drive the user's dev DB, so it was skipped. Parameterize the port/DB via env before
   including it in verification cycles.

## Baseline (cycle 1 = reference values for the next cycle)

| Metric | Value |
|---|---|
| Duplication | 0.84% lines / 1.36% tokens (10 clones) |
| Backend tests | 157 passed / 0 failed / 0 skipped (51+58+21+18+9) |
| Angular tests | 7 passed |
| API surface | 99 paths / 126 operations (`baseline-20260827/openapi.json`) |
| Migrations | 15 (`baseline-20260827/migrations-list.txt`) |
| Vulnerable packages | 0 High / 1 Moderate accepted (NuGet) · 7 High accepted w/ ADR (npm, Angular core) |

## Incomplete items (honest-reporting section)

- `dotnet list package --vulnerable` re-scan after pinning and `npm audit` re-run are **pending network**
  (api.nuget.org / registry.npmjs.org DNS failed mid-session). Offline evidence captured instead: resolved
  versions equal the advisory-fixed versions. Re-run both when the host resolves external DNS again.
- `docker` gates (postgres container, app image build) blocked by Windows-container daemon — substitute
  evidence recorded above; run the image build in CI (ubuntu runner).
- `gitleaks` unavailable (no binary, no linux containers) — substituted with a full-history `git grep`
  credential sweep (36 hits triaged, no real secret).
- Real-repo `dotnet format --verify` / restore currently fails on the dead network (implicit restore);
  the clone verified clean with identical file content. Re-run locally once network returns.
- CI on `main` was probably **red since ~Aug 25** on the stale encrypted-lab test — confirm on GitHub and
  re-push once these changes are committed.
- Gate 7 addendum below is limited to the Sample Tracking slice; remaining 20 modules pend future cycles
  (one slice per cycle per the pack).

## Gate 7 addendum — Sample Tracking slice review (adversarial, per pack §7)

Reviewed: `Features/SampleTracking/*`, its outbox handlers, `SampleTrackingQueries`/repository/configuration,
the domain aggregate, its endpoints and tests. Dependency direction, read-side purity, privilege declaration
(all 7 requests), write-side `EnsureAreaInScope` (all 4 commands), DTO-only projection, and structured
logging were checked and are **clean**. Findings:

| ID | Sev | Finding (all verified by reading code unless noted) |
|---|---|---|
| ST-1 | **Major** | **Receipts dispatched after midnight rollover are silently lost from derived tracking.** `VisitReceived` carries no date (`Domain/Operations/Events.cs:13`); `SampleReceiptTrackingHandler:37-38` returns *success* when the visit is already archived ("the day's roll-over totals stand" — false premise: `BoardService.RunRolloverAsync` never recomputes `sample_tracking`). Outbox drains every minute; any event in the final pre-midnight window (or during dispatcher backlog) is dropped permanently — bounded retry never engages because the handler doesn't fail. Fix: add `DateOnly VisitDate` to the event and recompute from `(area, date)` unconditionally (`SumReceivedSamplesAsync` already includes archived rows) |
| ST-2 | **Major** | `BatchRecordSampleDataEntryCommand` (`{}` body → `Lines = null`) throws NRE → **500** in the handler's `foreach`; sibling `SaveSampleAssignmentsValidator` guards the identical shape. Fix: add the validator, remove the ratchet entry. *(Binding-to-null inferred from standard STJ behavior, not executed.)* |
| ST-3 | Minor | `AdvanceSampleTrackingHandler` hand-throws `ValidationException` over bare `"Review"`/`"Sort"` literals — re-implements `ValidationBehavior`, magic strings shared with the endpoint. Fix: validator + enum/constants |
| ST-4 | Minor | `OperationsQueries.cs:354,369` hard-code `h.Status == "Received"` for history rows while binding `VisitStatus.Received` for live rows — an enumeration rename silently breaks derived totals. Fix: use `VisitStatus.Received.Name` |
| ST-5 | Minor | `LifecycleAsync` fetches **all areas'** tracking rows unscoped (`OperationsQueries.cs:322-325`); no response leak today (rows only surface via scope-filtered joins) but the protection is emergent, not enforced. Fix: apply the same area predicate as `ListAsync` |
| ST-6 | Minor | `OutboxDispatcher` catches a failed handler and still commits the **batch-level** `SaveChangesAsync` — a failed handler's *partial* mutations persist (heals only if a retry later succeeds; permanent after 5 attempts). Fix in dispatcher: save/rollback per message; the repository's `.Local` workaround could then shrink |
| ST-7 | Opinion | 3 sample-tracking routes bind Application commands directly as wire contracts while sibling routes use dedicated `...Body` records; `POST /sample-tracking` returns `200 {id}` vs siblings' `201`. Pick one convention |
| ST-8 | Opinion | `SaveSampleAssignmentsHandler` stamps lifecycle steps with caller-supplied free-text usernames — audit trail accepts arbitrary text. Validate against the user store if it must be trustworthy |
| ST-9 | Opinion | `SampleTracking` has 3 concurrent writer classes but no `IVersioned`/409; unique-index insert race surfaces as raw `DbUpdateException` → 500 (TransactionBehavior maps only concurrency exceptions); completed rows can be silently rewritten backwards |
| ST-10 | Opinion | `GetSampleLifecycleQuery` range unbounded (full-table scans possible); `"—"` placeholder baked into an Infrastructure projection |

**Duplication (slice):** the get-or-open upsert of an area/day row exists **five times** (both entry
handlers, assignments, receipt, area-change) — collapse into one helper; `RecordSampleDataEntryCommand`
duplicates the batch command with one line; the manual area-scope predicate is duplicated in
`ListAsync`/`ReportAsync` and belongs beside `ScopeFilter` (single scope-filtering site per the capability
inventory); the live∪archived "received visits" union is hand-rolled twice.

**Slice coverage gaps:** `SampleReceiptTrackingHandler` has **zero tests** (vs 9 for `AreaChangeTrackingHandler`);
no validator tests; scope tests cover 1 of 4 commands; no privilege-denial test; no concurrency test; no
`/sample-tracking` route appears in ApiTests or IntegrationTests; `SampleTrackingConfiguration` and the
repository's `.Local` logic are untested.

## Recommended CI additions (pack §CI)

Current CI covers Gate 1+2+4 partially. Add: `dotnet format --verify-no-changes` (Gate 3), jscpd with
`--threshold 3` + report artifact (Gate 3), `__DriftCheck` empty-migration assert (Gate 5),
`dotnet list package --vulnerable` + `npm audit --omit=dev` + gitleaks-action (Gate 6), and the docker
image build (Gate 1 tail). All are single-step jobs; wire them `needs: build`.

---
*Compliance status: conformant with the architect ruleset except the findings above. Assumptions: dev
cluster :5442 acceptable stand-in for a pristine postgres:17 container; test seeding password constants are
CI-only. Risks: Angular advisories until upgrade; jobs-pipeline bypass concentrates unaudited writes.
Limitations: network outage curtailed two re-scans; coverage union approximate. Verification status: every
number in this report comes from a command that exited 0 (or its failure is itself the reported finding).*
