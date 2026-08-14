# ADR-0004 — Hangfire for background jobs (replaces advisory-locked hosted services)

**Status:** Accepted · 2026-08-15

## Context
The reference runs four in-process background jobs (midnight board roll-over, evening missed-sweep +
notification dispatcher, Oracle sync, retention) as hosted services, each serialized on a single instance
by a distinct PostgreSQL advisory lock. The architect ruleset names **Hangfire** as the standard and
prescribes idempotent, retry-safe, outcome-recording jobs that invoke an application use case (no logic in
the job class). The SRS also mandates fixing reference defects JOBS-001/002/003/006.

## Decision
Use **Hangfire** (PostgreSQL storage) for scheduled/recurring work. Each recurring job is a thin class that
resolves an application use case (MediatR command) by **stable identifier** and does nothing else.
Single-execution semantics come from `[DisableConcurrentExecution]` + Hangfire's server locking (replacing
the hand-rolled advisory locks). Jobs are idempotent and retry-safe with bounded retries; every run records
a structured outcome and an audit entry (closing JOBS-002). Cron uses the Africa/Cairo timezone.

Recurring jobs:
- `board-rollover` — 00:00 Africa/Cairo.
- `missed-visit-sweep` — evening (e.g. 22:00), a **dedicated trigger independent of the morning path**,
  ordered before the archive (closes JOBS-001).
- `notification-dispatcher` — every 10s; auto-retry with backoff (closes JOBS-006); HTML email variables
  escaped (closes JOBS-003).
- `oracle-sync` — configured interval (default 24h); allow-list re-validated at run; writes audit (JOBS-002).
- `retention-purge` — every 24h.

## Alternatives considered
- **Keep hosted services + advisory locks:** faithful but reimplements scheduling, retries, dashboards, and
  is easier to get wrong (the reference's JOBS-001 defect is exactly this class of bug).
- **Quartz.NET:** capable, but Hangfire is the mandated default and ships a dashboard + storage-backed
  reliability out of the box.

## Consequences
- Adds a Hangfire schema to PostgreSQL and a secured dashboard (authorized, non-anonymous).
- Job outcomes/traces flow into Serilog + OpenTelemetry.
- The single-instance concurrency guarantee is preserved without bespoke locking.

## Risks
- Hangfire storage adds tables/migration surface. Acceptable; isolated in its own schema.

## Revisit criteria
Multi-instance scale-out (Hangfire already supports it) or a move to an external scheduler.
