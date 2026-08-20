# Follow-Up Management System — Build Plan & Progress

> Reconstruction of the Follow-Up Management System (spec: `01-srs.html`, `02-workflows.html`,
> `03-architecture.html`, `design-system.html`) as **Clean Architecture + SOLID + DDD**, governed by the
> **Enterprise Application Architect** ruleset (`Enterprise Application Architect.docx`).
> Stack: **.NET 8 · ASP.NET Core Web API · EF Core · PostgreSQL 17 · CQRS/MediatR · FluentValidation ·
> Serilog · OpenTelemetry · SignalR · Hangfire · Docker**; **Angular 22** (strict TS, standalone, signals,
> typed reactive forms, feature-based). Sequencing: **Domain + all backend first**, then Angular.

## Architectural rules (non-negotiable — from the architect ruleset)
- Dependency direction **Angular → Api → Application → Domain**; **Infrastructure → Application & Domain**.
  Domain references NOTHING (no EF, MediatR, ASP.NET, Serilog, Npgsql, HTTP, API models).
- **No** generic repositories, repository-per-table, exposed IQueryable, or redundant UoW over DbContext
  (ADR-0005). Aggregate repositories for writes; DTO-projecting read interfaces for queries.
- Never expose domain/persistence entities via API — request models, response DTOs, query projections only.
- CQRS via MediatR; pipeline behaviors: validation, auth-context, authorization, logging, tracing,
  transaction, **idempotency**, auditing, exception-mapping.
- Consistency: DB transaction per aggregate, optimistic concurrency, **Outbox** for domain events, idempotency keys.
- Server-authoritative authz (3 layers): default-deny routes → ~45 privileges → 6-dimension org scope +
  ownership; privileges/scope re-read from DB every request. Single-tenant (ADR-0002).
- Every state change writes exactly one immutable audit entry. Money = `numeric(18,2)`, recomputed server-side.
- Real-time via **SignalR** (ADR-0003); background jobs via **Hangfire** (ADR-0004); API under **/api/v1** (ADR-0006).
- E-signatures bind identity+authLevel+intent+meaning+recordId+**recordVersion**+timestamp+reason+contentHash+audit.
- Fix reference defects: JOBS-001, JOBS-002, JOBS-003, JOBS-006, SCOPE-READ, CMP-STAGE, ESIGN-UI.
- End substantial responses with Compliance/Assumptions/Risks/Limitations/Incomplete/Verification.
  Never claim build/test/migration success unless verified. Record conflicts as ADRs (docs/adr).

## Phases

### Phase 0 — Solution foundation  ✅ DONE
- [x] Solution + 4 layers + 2 test projects, dependency direction wired
- [x] Central package management, Directory.Build.props, global.json, .gitignore
- [x] Clean build, 0 warnings

### Phase 1 — Domain layer  ✅ DONE
- [x] SeedWork: Entity, AggregateRoot, ValueObject, Enumeration, IDomainEvent, DomainException, IAuditable, Money, GeoLocation
- [x] Status VOs w/ transition rules: LaboratoryStatus(8, inferred), VisitStatus(4), ComplaintStatus(3),
      MarketingVisitStatus(3), OutsourceStatus(3), RepresentativeType(4)+GoalDuration, SignatureMeaning(5),
      MarketingPurpose(7), Segment(A/B/C), ComplaintStage
- [x] VOs: LabCode (+ENC alias BR-7), VisitSchedule, TransferDetails, OrgScope (6-dim + anti-amplification), PasswordHash
- [x] Aggregates done: AppUser, Role (Identity) · Laboratory (+ContactPerson) · Representative ·
      DailyVisit · OutsourceSample · MarketingVisit · Complaint (+stages)
- [x] Privileges catalogue (~45) + expansion rules
- [x] Domain events (Lab/Visit/Complaint/Marketing)
- [x] Domain unit tests — 24 passing (state machines, scope, privilege expansion, lockout, lab code)
- [x] Remaining aggregates: UserSession, AuditEntry, ElectronicSignature (version-bound), VisitHistory,
      SampleTracking, MonthlySample, DailyLabStatistic, TestStatistic, TestGroup, TestSetup,
      LabLoyaltyLedger, RepCommission, CompensationConfig, RefItem/City/Area/AppSetting,
      NotificationTemplate/Preference/SystemNotification/DeliveryLog, OracleConfig (+AllowListedQuery)
- [x] Shared VOs: YearMonth. All 34 domain tests passing.

### Phase 2 — Application layer  ✅ DONE (all 21 modules; Infra-dependent behaviors deferred to Phase 3)
- [x] CQRS base: ICommand/IQuery/handlers, IAuthorizedRequest; application exceptions
      (NotFound/Forbidden/Unauthorized/Conflict/Validation)
- [x] Abstractions: ICurrentUser, IClock, IOutbox, IPasswordHasher/ITokenService,
      gateways (IEmailSender/IWhatsAppSender/IOracleReader/IMapLinkResolver), ILaboratoryRepository
- [x] Behaviors (Application-side): Authorization, Validation, Logging/Performance; AddApplication DI
- [x] Models: PagedResult/ListQuery (bounded 1000); ScopeGuard (record-level scope)
- [x] **Exemplar slice — Laboratories:** Create (uniqueness BR-1 + scope + contacts + schedule),
      ChangeStatus, GetLaboratories, GetLaboratoryById (+ ILaboratoryQueries read interface); 3 handler tests
- [x] **Representatives** module (create + list; repo + queries)
- [x] **Daily Board** module (check-in/miss/undo/verify commands with scope+ownership; board query + suggest;
      IDailyVisitRepository, IDailyBoardQueries)
- [x] **Complaints** module (log + start/resolve/reopen/stage; e-sign gate on resolve via IElectronicSignatureGate;
      list/detail/audit queries; IComplaintRepository) — 3 resolve-gate tests
- [x] Persistence interfaces added: IOperationsRepositories (visit/outsource/sample-tracking), IComplaintRepository
- [x] **Transfers** (FR-6): GetTransfers query + ConfirmTransfer command (driver details, scope+ownership)
- [x] **Lab check-in** (FR-7): GetLabCheckIn query + ConfirmReceipt command (derives lab Active)
- [x] **Outsource** (FR-9): list + create (unique per lab/date) + advance status + delete
- [x] **Marketing** (FR-10): list (scheduled-first BR-10) + schedule/complete/cancel
- [x] **Sample tracking** (FR-8): list + lifecycle report; data-entry (single/batch) + advance Review/Sort;
      area-scope enforcement (EnsureAreaInScope)
- [x] **Audit** (FR-20): GetAudit query (filtered, admin-only); IAuditQueries
- [x] **Platform** (FR-21): ResolveMapLink query (authenticated-only; SSRF guard in Infra resolver)
- [x] **User & role administration** (FR-2, BR-12): Roles (create/update/delete, built-in & in-use protection);
      Users (create/update/delete/unlock, self password/language); **anti-amplification** (privileges ⊆ caller,
      scope IsWithin caller) + self-role-change block; queries (users/lookup/roles). 4 security tests
- [x] **Reference & Setup** (FR-18): RefItem create/delete (dup guard), City create/delete, Area create/delete
      (transfer reps/transportation); list queries (refs/cities/areas, dropdown-open); repos + ISetupQueries
      — retention run + settings write deferred to pair with Infra
- [x] **Auth & sessions** (FR-1): Login (lockout, PBKDF2 verify, session+token issue, effective privileges/scope),
      Logout (revoke current session), GetMySessions; IAuthPolicy; ISessionQueries — 3 tests
- [x] **E-signature** (FR-19): SignRecord (re-auth + server-computed hash/version, meaning/reason binding),
      VerifySignature (tamper evidence); IRecordHasher, IElectronicSignatureRepository — 2 tests
- [x] **Loyalty & commissions** (FR-12, BR-9): CompensationCalculator domain service (loyalty tier/points,
      commission+bonus); SetLabTarget, RecalculateLoyalty, SaveCommission (server-side recompute),
      SetCompensationConfig; ledger/commission/config queries; ICompensationData/repos — 4 tests
- [x] **Lab stats** (FR-13): GetLabStats + ImportLabStats (xlsx via ISpreadsheetReader, upsert by date+code,
      summary with skipped/warnings)
- [x] **Test catalogue/stats** (FR-14): TestGroup CRUD (delete ungroups its setups), TestSetup CRUD,
      ImportTestStats; queries
- [x] **Insights** (FR-15): dashboard + 4 reports (overview/performance/lab-history/rep-intervals);
      IInsightsQueries; scope + encrypted-alias aware
- [x] **Notifications** (FR-16): feed (self), mark-read (ownership), mark-all, preferences get/update,
      gateways (masked)/logs/retry (RequeueForRetry, JOBS-006); notification repos
- [x] **Oracle integration** (FR-17): GetConfig (never returns connection string), UpdateConfig
      (enable+interval only), SyncNow (IOracleSyncRunner); IOracleConfigRepository
- [ ] Behaviors needing Infrastructure (Transaction, Idempotency, Audit, Outbox-dispatch) — Phase 3

Application progress: **21 / 21 modules COMPLETE**. 28 app + 37 domain = 65 tests passing.

### Phase 3 — Infrastructure layer  🟡 IN PROGRESS
- [x] EF Core DbContext (31 DbSets) + IEntityTypeConfiguration per aggregate (31 tables), indexes,
      FK policies (12 CASCADE / 8 RESTRICT / 1 SET NULL), strongly-typed-id + Enumeration + VO converters,
      `xmin` row-version concurrency on lab+rep, snake_case naming, 8 jsonb columns for VO collections
- [x] Migrations: InitialCreate (31 tables) + SchemaHardening (enum CHECK constraints + append-only
      audit-trail triggers refusing UPDATE/DELETE/TRUNCATE). **APPLIED & VERIFIED** against a live
      PostgreSQL 17 dev cluster (port 5442): 31 tables, 21 FKs, 8 CHECKs, 3 audit triggers; append-only
      immutability and CHECK rejection tested live. See docs/DEV-DATABASE.md.
- [x] Aggregate repository impls (26); TransactionBehavior = UoW (no IUnitOfWork wrapper); DbOutbox
- [x] AuditAndOutboxInterceptor: provenance stamps + immutable audit entry per change + domain-events→outbox,
      all in one transaction; outbox_message table + migration (AddOutbox, applied)
- [x] IClock (Cairo), PBKDF2-SHA256 hasher (100k), HMAC-SHA256 token service (~10h, secret from config),
      AuthPolicy; AddInfrastructure DI composition
- [x] **Integration tests (live DB):** create-lab persists state+contacts+audit+outbox atomically;
      duplicate rolls back with no partial state — 2 passing (67 total)
- [x] **Read/query implementations (18 query services)** — DTO projections, no IQueryable leak; ScopeFilter
      pushes the 6-dimension scope into SQL (incl. enum IN); ENC alias post-projection; dashboard/reports/insights
- [x] **Seed data** — 4 roles (Admin built-in + Ops/Collector/Marketing), admin login, 6 bilingual templates,
      compensation config (placeholder tiers), reference items; idempotent DatabaseSeeder
- [x] Read-path + seed integration tests (live DB): scope+segment SQL filtering, ENC masking, dashboard,
      seeder baseline + **admin login end-to-end** (PBKDF2→session→token), idempotency — 72 tests total
- [x] **Gateways:** SmtpEmailSender, WhatsAppSender (Meta), MapLinkResolver (SSRF host allow-list, no
      auto-redirect, 5s), XlsxSpreadsheetReader (hand-written, no lib), RecordHasher + ElectronicSignatureGate,
      ConfiguredOracleReader + OracleSyncRunner (enabled/due/allow-list/audit/status)
- [x] **Outbox dispatcher** (drains → MediatR notifications, bounded retries JOBS-006); DomainEventNotification
- [x] **Background jobs (Hangfire, ADR-0004):** BoardService (roll-over + evening missed-sweep JOBS-001),
      RetentionService (GUC-gated purge, summary-audit-first), 5 thin [DisableConcurrentExecution] job classes,
      RecurringJobsInitializer (Cairo cron); AddBackgroundJobs kept separate from AddInfrastructure
- [x] SystemCurrentUser for jobs/seeding (API overrides ICurrentUser)
- [x] Job/gateway integration tests (live DB): board generate+sweep, outbox drain, xlsx parse — 75 tests total
- [ ] ICurrentUser impl (API layer, HttpContext); sessions wiring in token-auth middleware, rate-limit store
- [x] **Notification fan-out** — DomainEventNotificationHandler maps events→template+recipients (by privilege),
      honours per-user channel prefs, writes the in-app feed + SignalR push + Mail/WhatsApp delivery logs;
      lab codes masked before external egress, email vars HTML-escaped (JOBS-003). **Verified live** (complaint
      → feed entry after the dispatcher) + an integration test
- [x] **Real-time broadcasts** — IRealtimeNotifier (SignalR in API, no-op elsewhere); TransactionBehavior
      pushes a `dataChange` hint after every committed command (Workflows §2.1); handler pushes per-user `notification`
- [ ] OpenTelemetry wiring (Serilog present via API); SignalR hub (Phase 4)
- [ ] Gateways: SMTP (escaped HTML), WhatsApp (Meta), Oracle (allow-listed SELECTs), Maps (SSRF-guarded)
- [ ] **Hangfire** jobs (ADR-0004): board-rollover, missed-sweep, notification-dispatcher, oracle-sync, retention
- [ ] **SignalR** hub (ADR-0003) + group authorization; xlsx reader
- [ ] Serilog + **OpenTelemetry** (HTTP, EF, MediatR, Hangfire, SignalR) wiring

### Phase 4 — API layer  🟡 MOSTLY DONE (running & verified on :5088)
> **Serves on port 5088** (NOT 5080 — busy on this host).
- [x] Middleware pipeline: ExceptionHandling→Problem Details (RFC 7807, no leak), Correlation, SecurityHeaders/CSP,
      Serilog request logging, CORS, static files, **TokenAuthMiddleware** (HMAC validate → session check →
      re-read privileges/scope from DB per request), ICurrentUser (HttpContext-backed; system principal for jobs)
- [x] Thin endpoint classes for all 21 modules under **/api/v1** (~105 mapped routes + 3 health); OpenAPI/Swagger
- [x] Health (live/ready/version), SignalR NotificationsHub (/hubs/notifications, authenticated), secured
      Hangfire dashboard (/jobs), OpenTelemetry tracing (ASP.NET Core + HttpClient), self-provision
      (Migrate + Seed on startup)
- [x] **Verified live over HTTP:** login (seeded admin → HMAC token + privileges/scope), auth gate (401),
      create/list labs, create complaint (CMP-1), dashboard, setup refs, RFC 7807 validation errors
- [ ] Automated API contract tests (WebApplicationFactory) — verified manually so far
- [ ] Rate limiting on login; static SPA fallback (pairs with Phase 5 Angular build)

### Phase 5 — Angular frontend  ✅ DONE (runs vs live API; e2e/maps deferred)
> Angular **19** (Node 20.18.1 caps at 19; ADR-0007 — forward-compatible with 22).
- [x] Scaffolded `web/` (standalone, signals, strict TS, SCSS); design-system tokens in styles.scss
      ("Arfa Corporate Blue" — full light+dark palette, Encode Sans/Cairo/IBM Plex Mono, RTL-ready)
- [x] Core: AuthService (signals, token persist), authInterceptor (Bearer + 401→login), authGuard,
      UiService (theme + EN/AR dir on <body>), typed models, StatusBadgePipe
- [x] App shell (64px header + logo chip + theme/lang toggles + sign-out, 240px rail), lazy-loaded routes
- [x] Features: Login (typed reactive form), Dashboard (KPIs + schedule + complaints), Labs (paged search),
      Complaints (list) — all wired to /api/v1 via proxy
- [x] Single-service integration: `ng build` → API wwwroot; API serves SPA + deep-link fallback on **:5088**
      (default-deny preserved for unmapped /api)
- [x] Verified live: SPA served, proxy forwards, admin login returns token+privileges, assets 200, deep links 200
- [x] **i18n (EN/AR)** — I18nService + impure `t` pipe + bilingual dictionary, wired into shell/labs/screens (RTL flips on <body>)
- [x] **Screens:** Daily Board (check-in/miss/verify actions, privilege-gated), Representatives, Marketing,
      Notifications (mark read/all), Reports (overview KPIs + rep performance), Setup (ref-data by type),
      Users; **Lab create form** (typed reactive form) — all via centralized ApiService
- [x] **SignalR client** (RealtimeService: access-token factory, auto-reconnect, dataChange→tick signal);
      shell shows live indicator + starts/stops connection
- [x] Centralized ApiService; privilege-gated nav; CSP updated to allow Google Fonts + ws/wss
- [x] **Angular unit tests (7, headless Chrome): StatusBadgePipe + AuthService** — passing
- [x] Fixed Insights overview (Money.Amount can't translate → materialize+sum). Full app builds; single
      service on :5088 serves SPA + all endpoints (overview/performance/reps/daily/users verified)
- [ ] Deferred: Leaflet maps (lab geo), e2e (Playwright/Cypress), remaining create/edit forms + e-sign UI,
      full notification fan-out UI

### Phase 6 — Cross-cutting & delivery  🟡 DONE (image not built here — no Docker daemon)
> **Publish/run on port 5088, NOT 5080.**
- [x] **Architecture-conformance tests** (NetArchTest, 5) — dependency direction Api→Infra→App→Domain +
      Domain/Application framework-freedom; CI-gated, passing
- [x] Integration tests (10, live DB); backend total **80 tests** (+7 Angular = 87)
- [x] **Dockerfile** (multi-stage: Angular build → .NET publish with SPA in wwwroot → aspnet runtime,
      non-root, EXPOSE 5088, ASPNETCORE_URLS=http://+:5088) + `.dockerignore`
- [x] **docker-compose.yml** (app + PostgreSQL 17, healthcheck, self-provision on startup, `-p 5088:5088`)
- [x] **CI** (.github/workflows/ci.yml): backend job (Postgres service, restore/build/migrate/test incl.
      architecture) + frontend job (npm ci / ng build / ng test headless)
- [x] README (stack, layout, dev + Docker run, tests, reference-build corrections)
- [ ] NOT executed here: `docker build`/`compose up` (no Docker daemon on this host) and the CI run
      (validated by local equivalents: full `dotnet test` + `ng build`/`ng test` all green)

## Current position
ALL PHASES COMPLETE (0–6). Domain + Application (21 modules) + Infrastructure (EF/migrations/repos/queries/
gateways/Hangfire/SignalR/OTel/outbox/audit/seed) + API (/api/v1, full pipeline, health, SignalR, Hangfire
dashboard) + Angular SPA (i18n, all screens, SignalR client) + delivery (Dockerfile, compose, CI, arch tests).
**87 tests** (37 domain + 28 application + 5 architecture + 10 live integration + 7 Angular). Runs as a single
service on **:5088** (SPA + API + jobs), verified end-to-end against a live PostgreSQL 17.

**Post-completion hardening:** notification fan-out + real-time `dataChange`/`notification` broadcasts are now
implemented and verified live (was the #1 gap). Docker image build verified in WSL2.

**Hardening #2 done (2026-08-16):** UpdateLaboratory + UpdateRepresentative (optimistic-concurrency 409 via
xmin + app-level version check; DbUpdateConcurrencyException→409 in TransactionBehavior), lab image upload
(content-sniff JPEG/PNG, 5 MB cap, GUID name, /uploads volume served), settings (GET/PUT, secret masking),
retention (GET/PUT min-30, POST run). 4 new integration tests (85 backend total). Route surface now ~113/116.

**Hardening #4 done (2026-08-16):** cross-cutting hardening.
- **IdempotencyBehavior** — `Idempotency-Key` header → `IIdempotencyKeyProvider` (Http impl in Api, Null default
  in Infra). Registered innermost, *inside* the transaction (after TransactionBehavior). First call runs and
  persists the serialized response to `idempotency_record` (jsonb); a retry with the same key replays the cached
  response instead of re-executing. Migration `AddIdempotency` applied. Verified live: two `POST /labs` with the
  same key return the same id (no duplicate-code 409).
- **Login rate-limiting** — `AddRateLimiter` fixed-window policy `"login"`, per-IP 10/min → 429; `.RequireRateLimiting`
  on the login endpoint. Verified live: 12 rapid logins → `401×9, 429×3`.
- **Missing test categories** — `ValidatorTests` (CreateLaboratory/CreateUser/SetRetention),
  `AuthorizationBehaviorTests` (unauthenticated→401, missing-privilege→403, held→pass),
  `IdempotencyTests` (same-key dedup). Full suite green: Domain 37, Application 35, Architecture 5,
  Integration 16 = **93 backend** + 7 Angular = 100.

**Frontend depth #3 (2026-08-16):** filling the Angular screen gaps in batches (all build clean, 7 unit tests green).
- **Batch A** — live notification badge (`NotificationStore`, unread count refreshed on real-time tick);
  complaints workflow (log-complaint form + start/resolve/reopen/advance-stage); marketing (schedule form +
  complete/cancel).
- **Batch B** — laboratory detail/edit screen: profile + contacts view, inline edit carrying `RowVersion`
  (409 → reload prompt), status change, PNG/JPEG image upload; list rows open the detail.
- **Batch C** — transfers (confirm form), lab check-in (confirm receipt), sample-tracking (new entry +
  Review/Sort advance). Nav entries EN/AR + lazy routes. All three GET endpoints + SPA verified 200 live.

- **Batch D** — **Settings** admin screen (app settings with write-only secret handling + data-retention
  panel: set window min-30, run purge now) and **Analytics** screen (tabbed Loyalty / Commissions / Lab
  statistics with period + date-range filters; lab/rep name maps; per-tab privilege gating). Shell nav gained
  `anyOf` privilege support. All five endpoints verified 200 live; secrets confirmed masked (`********`).

- **Batch E** — **Test Catalogue** screen (groups + setups inline CRUD with per-action privilege gating) and a
  reusable **e-signature panel** (`EsignPanelComponent`, module+recordId): shows signature status + validity,
  applies a signature via password re-auth (server re-authenticates). Embedded as an expandable row in the
  complaints screen. Verified live: catalogue CRUD 200/204; sign 200 → verified valid; wrong password → 403.

- **Batch F** — **Leaflet maps** on the laboratory detail. Reusable `MapComponent` (OSM tiles + vector
  `circleMarker`, no image-asset dependency; `invalidateSize` after layout; graceful if tiles can't load).
  Read view shows the lab location + an "open in OpenStreetMap" link; edit view adds lat/lng inputs, a
  click-to-drop editable map, and a **paste-a-maps-link resolver** that parses coordinates client-side
  (`@lat,lng`, `q=`, `!3d!4d`, `mlat/mlon`) and falls back to the API `/maps/resolve-redirect`. `leaflet` +
  `@types/leaflet` added; `leaflet.css` wired into angular.json; bundles only into the lazy lab-detail chunk.
  Verified live: `/maps/resolve-redirect` 200 and coordinate round-trip PUT 204 → GET returns the coords.
  (Live OSM tile rendering not verifiable in this sandbox — the browser pane can't reach host localhost.)

Remaining polish (not blocking): Playwright e2e suite, API contract tests (WebApplicationFactory).
