# Capability Inventory

Single source of truth for every shared abstraction, base class, value object, pipeline behavior and
cross-cutting service in the Follow-Up codebase. **Attach this file to any prompt/spec that asks for a new
module** — most duplicate code is written because the author (human or model) could not see what already
existed. Update it in the same PR that adds or removes a capability.

Verified against the codebase on 2026-08-27 (conformance verification cycle 1).

## Domain building blocks — `FollowUp.Domain.Common`

| Type | Purpose |
|---|---|
| `Entity<TId>` | Identity equality + private domain-event buffer (`Raise`, `DomainEvents`, `ClearDomainEvents`) |
| `AggregateRoot<TId>` | Consistency boundary marker; only roots get repositories |
| `ValueObject` | Equality-by-components base; all 9 implementations are sealed + immutable |
| `Enumeration` | Closed value sets (12 derived types) |
| `DomainEvent` / `IDomainEvent` | Base record for events drained to the outbox |
| `IHasDomainEvents` | Interceptor hook for audit + outbox drain |
| `IAuditable` | `CreatedAt/CreatedBy/ModifiedAt/ModifiedBy` stamped by the save interceptor |
| `IVersioned` | `uint RowVersion` → mapped to Postgres `xmin` (optimistic concurrency). Currently: `Laboratory`, `Representative` |
| `DomainException` (+ `IllegalStateTransitionException`) | Invariant violations → 400/409 via the API middleware |
| `Money`, `YearMonth`, `GeoLocation` | Common value types (note: `Money`/`YearMonth` do not derive from `ValueObject`; they have dedicated EF converters) |

Value objects elsewhere in Domain: `OrgScope`, `PasswordHash` (Identity), `LabCode`, `VisitSchedule`
(Laboratories), `TransferDetails`, `TrackingStep` (Operations), `LoyaltyTier` (Compensation),
`AllowListedQuery` (Integration).

## CQRS messaging — `FollowUp.Application.Common.Messaging`

| Type | Purpose |
|---|---|
| `IBaseCommand` | Non-generic write marker; gates `TransactionBehavior` + `IdempotencyBehavior` |
| `ICommand` / `ICommand<TResponse>` | Write requests (75 commands) |
| `IQuery<TResponse>` | Read requests (47 queries); handlers must stay side-effect-free |
| `ICommandHandler<,>` / `IQueryHandler<,>` | Handler contracts (135 handlers, all in Application) |
| `IAuthorizedRequest` | `RequiredPrivileges` consumed by `AuthorizationBehavior` |
| `DomainEventNotification` | MediatR notification wrapper published by the outbox dispatcher |

Naming: `<Name>Command`/`<Name>Query` + `<Name>Handler` + `<Name>Validator`, colocated per feature slice
under `Features/<Module>/`.

## Pipeline behaviors (exactly these five — do not add a competing one per concern)

| Behavior | Layer | Concern |
|---|---|---|
| `AuthorizationBehavior` | Application | `IAuthorizedRequest` privilege check (throws `UnauthorizedException`/`ForbiddenException`) |
| `ValidationBehavior` | Application | FluentValidation → `ValidationException` |
| `LoggingBehavior` | Application | Timing + correlation, slow-request warning (>1000 ms) |
| `TransactionBehavior` | Infrastructure | One transaction per `IBaseCommand`; maps `DbUpdateConcurrencyException` → `ConflictException`; realtime data-changed hint after commit |
| `IdempotencyBehavior` | Infrastructure | Replays stored response for a seen `Idempotency-Key` |

## Cross-cutting abstractions — `FollowUp.Application.Common.Abstractions`

| Interface | Implementation(s) | Notes |
|---|---|---|
| `IClock` | `Infrastructure.Time.SystemClock` | `UtcNow`, `CairoNow`, `CairoToday` — never use `DateTime.UtcNow` for business "today" |
| `ICurrentUser` | `Api.Auth.CurrentUser` (HTTP) + `Infrastructure.Security.SystemCurrentUser` (jobs/seed, `TryAdd` default) | Intentional pair |
| `IIdempotencyKeyProvider` | `Api.Auth.HttpIdempotencyKeyProvider` + `NullIdempotencyKeyProvider` (default) | Intentional pair |
| `IRealtimeNotifier` | `Api.Realtime.SignalRRealtimeNotifier` + `NullRealtimeNotifier` (default) | Intentional pair (null-object) |
| `IOutbox` | `Persistence.Outbox.DbOutbox` | Explicit enqueue; interceptor drains aggregate events automatically |
| `IPasswordHasher` / `ITokenService` / `IAuthPolicy` / `IRecordHasher` | `Pbkdf2PasswordHasher` / `HmacTokenService` / `AuthPolicy` / … | Security primitives |
| `IEmailSender`, `IWhatsAppSender`, `IOracleReader`, `IMapLinkResolver` | `Infrastructure.Gateways.*` | External gateways |
| `IFileStorage`, `ISpreadsheetReader`, `IElectronicSignatureGate`, `INotificationRecipients`, `IOracleSyncRunner` | Infrastructure | |

## Security helpers — `FollowUp.Application.Common.Security`

`ScopeGuard` (`EnsureInScope`, `EnsureHierarchyInScope`, `EnsureOwnedIfRepLinked`, `EnsureAreaInScope`) and
`AntiAmplificationGuard` (`EnsurePrivilegesWithinGrant`, `EnsureScopeWithinGrant`). Org-scope filtering in
SQL: `Infrastructure.Persistence.ScopeFilter.ApplyScope(...)` — every list/read query must apply it.

## Persistence

- `FollowUpDbContext` (31 DbSets, snake_case, converters in `ConfigureConventions`).
- `AuditAndOutboxInterceptor` — the ONE save interceptor: audit stamps + `AuditEntry` rows + outbox drain.
- Repositories: 27 aggregate-oriented interfaces in `Application.Common.Abstractions.Persistence`
  (no generic repository — forbidden). Implementations in `Infrastructure.Persistence.Repositories`.
- Read side: 19 per-module `I*Queries` interfaces declared in their feature slice, implemented in
  `Infrastructure.Persistence.Queries.*`, projecting straight to DTOs (no `IQueryable` leaks).
- Idempotency store: `IdempotencyRecord`; Outbox store: `OutboxMessage` + `OutboxDispatcher`
  (batch 100, max 5 attempts).

## API layer

- Minimal-API endpoint classes under `Api/Endpoints` (8 files, 126 routes, `/api/v1`).
- `ExceptionHandlingMiddleware` — the ONE exception→ProblemDetails mapping site.
- `PlatformMiddleware.cs` — `CorrelationMiddleware`, `SecurityHeadersMiddleware`.
- `TokenAuthMiddleware` + edge default-deny for `/api/v1` (401 without a resolved principal).
- Rate limiting: policy `"login"` (fixed window 10/min per IP) — extend here, don't invent a second scheme.
- `NotificationsHub` (`/hubs/notifications`): no client-invokable methods; per-user groups derived
  server-side (`user:{id}`).

## Background jobs — `Infrastructure.Jobs`

`BoardRolloverJob`, `MissedSweepJob`, `NotificationDispatchJob`, `OracleSyncJob`, `RetentionJob` — thin
classes over `BoardService`, `OutboxDispatcher`, `OracleSyncRunner`, `RetentionService`;
`RecurringJobsInitializer` owns the cron registrations (Africa/Cairo).

## Test doubles worth reusing

`tests/FollowUp.Application.Tests/Common/Fakes.cs` — ~18 in-memory fakes (`FakeLaboratoryRepository`,
`FakeCurrentUser`, `FakeClock`, …). `tests/FollowUp.ApiTests/ApiFixture.cs` — authenticated
`WebApplicationFactory` fixture. `tests/FollowUp.IntegrationTests/IntegrationFixture.cs` — real DI graph
against `FOLLOWUP_DB`.

## Architecture tests (CI-gated)

`tests/FollowUp.ArchitectureTests`: `DependencyDirectionTests` (layering), `DomainModelTests`
(encapsulation, EF ctors, VO immutability, concurrency tokens, offline model build),
`CqrsConventionTests` (handlers-in-Application, validator ratchet, privilege-declaration ratchet,
read-side purity, no `IQueryable`, no generic repository), `ApiAndInfrastructureRulesTests`
(endpoint purity ratchets, hub surface, job rules). Ratchet allowlists pin known findings — never add
entries; remove them as gaps are fixed.
