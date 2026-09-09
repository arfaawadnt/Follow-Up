# ADR-0004 — Hangfire for background jobs (replaces advisory-locked hosted services)

**Status:** Accepted · 2026-08-15 · **Revised 2026-09-09** (records the implemented job→service delegation; see *Revision note*)

## Context
The reference runs its background work (midnight board roll-over, evening missed-sweep, notification
dispatch, Oracle sync, retention) as hosted services, each serialized on a single instance by a distinct
PostgreSQL advisory lock. The architect ruleset names **Hangfire** as the standard and prescribes
idempotent, retry-safe, outcome-recording jobs that **invoke an application use case with no logic in the
job class**. The SRS also mandates fixing reference defects JOBS-001/002/003/006.

## Decision
Use **Hangfire** (PostgreSQL storage) for scheduled/recurring work. Each recurring job is a **thin class**
that resolves a **dedicated job service by stable identifier and does nothing else** — it takes only
primitive/id arguments, never a domain entity, and never owns persistence (no `DbContext`). This is enforced
by `ArchitectureTests.ApiAndInfrastructureRulesTests.Hangfire_jobs_take_no_entities_and_own_no_persistence`.
Single-execution semantics come from `[DisableConcurrentExecution]` + Hangfire's server locking (replacing
the hand-rolled advisory locks). Jobs are idempotent and retry-safe with bounded retries, and every run
records a structured outcome. Cron uses the Africa/Cairo timezone.

The job services (`BoardService`, `OracleSyncRunner`, `OutboxDispatcher`, `RetentionService`,
`SegmentAssignmentRunner`, `NotificationDeliveryRetryRunner`, `StatsEmailRunner`) live in Infrastructure
because they are **system batch/orchestration** operations — they iterate Oracle feeds, sweep date windows,
drain the outbox, purge by cut-off — driven by the scheduler, not by an authenticated request.

### Jobs run as services, not MediatR commands — and why
Background jobs deliberately do **not** flow through the MediatR command pipeline (the earlier revision of
this ADR mis-stated that they did — finding M-JOB/M-3). The pipeline exists to wrap a **user request**, and
a scheduled job is not one:

- **Authorization / Validation behaviors don't apply** — there is no authenticated principal and no
  request payload to validate; a job runs with system authority on a fixed schedule.
- **The work is batch orchestration, not a single use case** — e.g. the Oracle sync re-validates the
  allow-list, runs feeds in dependency order, upserts, and records status; the outbox dispatcher processes a
  batch, each message in its own scope + transaction. These do not map to one command/handler.
- **The cross-cutting concerns the pipeline would provide are met directly**, so nothing is lost:
  - *Audit* — the `AuditAndOutboxInterceptor` writes audit entries (and outbox rows) on every `SaveChanges`,
    regardless of the caller, so job mutations are audited (closes JOBS-002).
  - *Transactions* — each service owns its boundary explicitly (`BeginTransaction`/`Commit`, or the
    per-message scope + transaction in `OutboxDispatcher`), so a failure rolls back cleanly and a retry
    re-runs without duplicating side effects.
  - *Tracing* — DB and app spans flow from the `Npgsql` and `FollowUp` `ActivitySource`s registered in
    OpenTelemetry, independent of the MediatR `TracingBehavior`.
  - *Structured outcome* — every run logs a summary via Serilog and, where applicable, records status on the
    aggregate (`OracleConfig.LastStatus`/`LastStatsStatus`).

"No logic in the job class" — the substantive rule — is satisfied: the Hangfire class is a thin delegator;
the orchestration lives in a dedicated, individually-testable service.

### Recurring jobs (Africa/Cairo where date-sensitive)
- `board-rollover` — `0 0 * * *`; `missed-visit-sweep` — `0 22 * * *`, a dedicated evening trigger ordered
  before the archive (closes JOBS-001).
- `notification-dispatcher` — every minute; drains the outbox, each message in its own transaction
  (idempotent, bounded retries, dead-letter surfaced at the attempt limit).
- `notification-delivery-retry` — every 5 min; re-sends failed deliveries from their stored content.
- `oracle-sync` — hourly trigger, the runner gates on the configured interval; allow-list re-validated at
  run; audited (JOBS-002). Date-scoped statistics feeds run on their own windows via `nightly-stats-sync`
  (`0 0 * * *`) and the page buttons, recording a separate stats-sync status without moving the hourly
  due-gate.
- `retention-purge` — `0 3 * * *`; also sweeps abandoned uploads and expired idempotency keys.
- `monthly-segment-assignment` — `0 2 1 * *`; plus per-subscription stats-email schedules.

HTML email variables are escaped (closes JOBS-003).

## Alternatives considered
- **Keep hosted services + advisory locks:** faithful but reimplements scheduling, retries, dashboards, and
  is easier to get wrong (the reference's JOBS-001 defect is exactly this class of bug).
- **Route jobs through MediatR commands (as the original ADR text prescribed):** uniform with request
  handling, but adds a command + handler per job purely as ceremony — jobs have no user, auth, or request
  validation, and audit/transactions/tracing are already covered — so it buys little for real refactor cost.
- **Quartz.NET:** capable, but Hangfire is the mandated default and ships a dashboard + storage-backed
  reliability out of the box.

## Consequences
- Adds a Hangfire schema to PostgreSQL and a secured dashboard (authorized, non-anonymous, local-only).
- Job outcomes/traces flow into Serilog + OpenTelemetry.
- The single-instance concurrency guarantee is preserved without bespoke locking.
- Job orchestration lives in Infrastructure services rather than Application handlers; they remain thin,
  dependency-injected, and unit/integration-testable in isolation.

## Risks
- Hangfire storage adds tables/migration surface. Acceptable; isolated in its own schema.

## Revisit criteria
Multi-instance scale-out (Hangfire already supports it) or a move to an external scheduler. Also revisit if a
job ever needs request-shaped validation/authorization — that job should then become a MediatR command.

## Revision note (2026-09-09)
The original *Decision* text stated each job "resolves an application use case (MediatR command)", which the
implementation never did — it delegates to Infrastructure job services (finding M-JOB/M-3, a doc↔code
contradiction). This revision records the actual, deliberate design and the rationale above; no code changed.
The substantive architect rule ("no logic in the job class") was and remains enforced by the architecture
test suite.
