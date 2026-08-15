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

### Phase 2 — Application layer  🟡 IN PROGRESS
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
- [ ] Remaining modules (Application): Loyalty/commissions, Lab stats, Test catalogue/stats,
      Insights (dashboard/reports), Notifications, Oracle integration
- [ ] Behaviors needing Infrastructure (Transaction, Idempotency, Audit, Outbox-dispatch) — Phase 3

Application progress: **15 / 21 modules**. 23 app tests.

### Phase 3 — Infrastructure layer
- [ ] EF Core DbContext + IEntityTypeConfiguration per aggregate (31 tables), CHECK constraints, indexes,
      FK policies (RESTRICT/CASCADE/SET NULL), strongly-typed-id + Enumeration + VO converters, row-version
- [ ] Aggregate repository impls; TransactionBehavior = UoW (no IUnitOfWork wrapper); read/query impls
- [ ] **Outbox** table + dispatcher; domain-event → MediatR notification adaptation
- [ ] Migrations (idempotent provisioning), audit immutability triggers, seed (roles, 6 templates, refs)
- [ ] Auth: PBKDF2-SHA256 (100k), HMAC-SHA256 token (~10h), sessions, lockout, rate-limit store
- [ ] Gateways: SMTP (escaped HTML), WhatsApp (Meta), Oracle (allow-listed SELECTs), Maps (SSRF-guarded)
- [ ] **Hangfire** jobs (ADR-0004): board-rollover, missed-sweep, notification-dispatcher, oracle-sync, retention
- [ ] **SignalR** hub (ADR-0003) + group authorization; xlsx reader
- [ ] Serilog + **OpenTelemetry** (HTTP, EF, MediatR, Hangfire, SignalR) wiring

### Phase 4 — API layer
- [ ] Middleware pipeline (exception→Problem Details, security headers/CSP, correlation, rate-limit,
      token auth, default-deny + privilege, endpoint + scope)
- [ ] Thin endpoint classes per module under **/api/v1**; route policy table (116 routes); OpenAPI + versioning
- [ ] Health endpoints (live/ready/version); static SPA hosting; SignalR hub mapping; secured Hangfire dashboard

### Phase 5 — Angular frontend
- [ ] App shell (64px header, 240px rail), design-system tokens, EN/AR + RTL, light/dark
- [ ] Auth, module screens, SSE client, maps

### Phase 6 — Cross-cutting & delivery
- [ ] Integration tests, Dockerfile (multi-stage), CI (build/test/architecture-conformance), seed & run on :5080

## Current position
Phase 1 (Domain) COMPLETE — every bounded context modeled: ~30 aggregates/entities, all state machines,
the authorization model (OrgScope + Privileges), value objects, domain events; 34 passing tests; clean
build under warnings-as-errors. Next: Phase 2 (Application) — abstractions, MediatR pipeline behaviors,
then commands/queries per module.
