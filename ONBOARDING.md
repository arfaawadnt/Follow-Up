# Onboarding — Follow-Up Management System

Welcome! This guide gets you from a fresh clone to a running app, passing tests, and productive changes.
Read it top to bottom once; after that it's a reference.

The Follow-Up system is a **single-tenant, bilingual (EN/AR, RTL) B2B field-operations platform** for a
medical-lab group — 21 modules, 31 tables, 116 routes, a 3-layer authorization model, and 4 background jobs.
It's built to the *Enterprise Application Architect* ruleset: **Clean Architecture + SOLID + DDD + CQRS**.

---

## 1. Prerequisites

| Tool | Version | Notes |
|------|---------|-------|
| .NET SDK | **8.0.x** | Backend |
| Node.js | **20.x** | Angular 19 caps below 22 on Node 20 (ADR-0007) |
| PostgreSQL | **17** | Local cluster or Docker |
| Docker | recent | Optional — for the one-command run |

---

## 2. Quickstart

### Option A — Docker (simplest; brings its own PostgreSQL)
```bash
export FOLLOWUP_AUTH_SECRET="a-strong-random-secret-at-least-32-chars"
docker compose up --build          # → http://localhost:5088
```
The image builds the SPA, publishes the API, runs as non-root, and **self-provisions the schema + seed** on
startup. Log in as `admin` (password = `FOLLOWUP_ADMIN_PASSWORD`, default `ChangeMe_Admin_2026!`).

### Option B — Local dev (backend + live-reload SPA)
```bash
# 1. Point at a PostgreSQL 17 and set a signing secret
export FOLLOWUP_DB="Host=127.0.0.1;Port=5442;Database=followup;Username=postgres;Password=<pw>"
export FOLLOWUP_AUTH_SECRET="a-strong-random-secret-at-least-32-chars"
export FOLLOWUP_ADMIN_PASSWORD="Seed_Admin_2026!"   # optional; sets the seeded admin's password

# 2. API — applies migrations + seeds on startup, serves on :5088
dotnet run --project src/FollowUp.Api

# 3. SPA — live reload, proxies /api and /hubs to :5088
cd web && npm ci && npx ng serve    # → http://localhost:4200
```
> The API **always serves on `http://localhost:5088`** (5080 is reserved on the target host); override with
> `ASPNETCORE_URLS`. In production the SPA is built into the API's `wwwroot` and served same-origin.

### Local database
A dedicated dev cluster lives at `127.0.0.1:5442` (db `followup`, user `postgres`). It is **not** a Windows
service — start it manually. Full details, including start/stop and verification, are in
[docs/DEV-DATABASE.md](docs/DEV-DATABASE.md). The password is supplied out-of-band and lives **only** in your
`FOLLOWUP_DB` env var — never in source.

---

## 3. Environment variables

| Variable | Purpose | Notes |
|----------|---------|-------|
| `FOLLOWUP_DB` | Connection string | Read by the API and the EF design-time factory. Empty = treated as absent. |
| `FOLLOWUP_AUTH_SECRET` | HMAC token signing secret | Required for token issuance/validation. |
| `FOLLOWUP_ADMIN_PASSWORD` | Seeded admin password | Default `ChangeMe_Admin_2026!`. Set to `Seed_Admin_2026!` to match the tests. |

Config precedence: `ConnectionStrings:FollowUp` / `Auth:SigningSecret` in appsettings win over the env vars.
Secrets are never committed.

---

## 4. Architecture at a glance

```
src/
  FollowUp.Domain          aggregates, value objects, smart-enum state machines, domain events — ZERO deps
  FollowUp.Application      CQRS commands/queries (MediatR), validators, abstractions, pipeline behaviors
  FollowUp.Infrastructure   EF Core, repositories, gateways, Hangfire jobs, auth, outbox + audit interceptor
  FollowUp.Api              middleware pipeline, /api/v1 endpoints, SignalR hub, SPA hosting
web/                        Angular 19 SPA (built into the API wwwroot)
tests/                      Domain · Application · Integration (live DB) · Architecture · ApiTests (HTTP)
docs/                       BUILD-PLAN, ASSUMPTIONS, ADRs, DEV-DATABASE
```

**Dependency direction (CI-gated by NetArchTest): `Api → Infrastructure → Application → Domain`.** Domain
depends on nothing. If you add a reference that points "inward-out", the architecture tests fail — that's
intentional.

**Request pipeline (MediatR behaviors), outermost → innermost:**
`Logging → Validation (FluentValidation) → Authorization → Transaction → Idempotency → handler`. The
Transaction and Idempotency behaviors live in Infrastructure (they need the DbContext); the rest are in
Application.

**Key patterns you'll meet immediately:**
- **CQRS**: every use case is a `Command`/`Query` record + handler. Endpoints are thin — they just `Send`.
- **Strongly-typed IDs** (`record struct` wrapping `Guid`) with EF value converters.
- **Smart enums** (`Enumeration` base) with transition guards — status changes go *through the state machine*,
  never by direct assignment (see CMP-STAGE).
- **Outbox + append-only audit**: domain events are dispatched via an outbox; audit rows are written by a
  SaveChanges interceptor and DB triggers, and can only be deleted through the bounded retention purge.
- **Optimistic concurrency** via Postgres `xmin` (`RowVersion`); updates carry the version and get a `409` on
  conflict.
- **Auth**: PBKDF2 password hashing, HMAC bearer tokens (~10h), DB-backed sessions, per-account lockout,
  per-IP login rate limiting. Privileges + org scope are **re-read from the DB on every request** — never
  trusted from the token. Every `/api/v1` route requires an authenticated principal except `/auth/login`.

---

## 5. Running the tests

```bash
# Backend — 102 tests across 5 projects (Integration + ApiTests need FOLLOWUP_DB set; they SKIP without it)
dotnet test FollowUp.sln

# Frontend unit tests — 7 tests, headless Chrome
cd web && npx ng test --watch=false --browsers=ChromeHeadless

# End-to-end — 4 Playwright specs; the config auto-launches the API on :5088
cd web && npx playwright install chromium   # once
FOLLOWUP_DB=... FOLLOWUP_AUTH_SECRET=... FOLLOWUP_ADMIN_PASSWORD=Seed_Admin_2026! npm run e2e
```

| Suite | Project | Needs DB? |
|-------|---------|-----------|
| Domain | `tests/FollowUp.Domain.Tests` | no |
| Application | `tests/FollowUp.Application.Tests` | no |
| Architecture (dependency direction) | `tests/FollowUp.ArchitectureTests` | no |
| Integration (real DI graph + DB) | `tests/FollowUp.IntegrationTests` | yes |
| API contract (in-process HTTP) | `tests/FollowUp.ApiTests` | yes |

DB-backed tests use the live dev database and reset the relevant tables between runs (in FK-safe order). They
are hermetic across runs — if you add a new persistent table a test writes to, remember to clear it in
`IntegrationFixture.ResetAsync`.

---

## 6. CI

`.github/workflows/ci.yml` runs on every push to `main` and every PR (badge is on the README). Three jobs, all
on a `postgres:17` service container:

1. **Backend** — build, provision the DB (runs the API until healthy so it migrates + seeds), then all 5 test
   projects.
2. **Frontend** — build + Karma unit tests (no-sandbox Chrome via `web/karma.conf.js`).
3. **E2E** — build API + SPA, install Playwright chromium, run the specs; uploads the report on failure.

Green CI is the bar for merge.

---

## 7. Conventions & gotchas

- **EF + value objects don't fully translate.** Sub-properties of a value object / converted enum
  (`l.Code.Value`, `s.Income.Amount`) **do not** translate to SQL. Project the converted object first, then map
  in memory. Converted-type *equality* (`l.Status == value`) does translate. This bites everyone once.
- **Status transitions go through the state machine.** Don't set a status property directly; call the domain
  method so the guard runs.
- **Reads are org-scoped.** Query handlers filter by the caller's `OrgScope`; an anonymous/denied scope returns
  empty, not everything.
- **Idempotency**: send an `Idempotency-Key` header on a command POST to make retries safe — the response is
  replayed, not re-executed.
- **Frontend**: standalone components, signals, typed reactive forms, lazy routes. API calls go through
  `core/api.service.ts`; auth state + privilege checks (`auth.has(...)`) live in `core/auth.service.ts`; the
  server still enforces everything — the client only hides UI. Bilingual strings are in `core/i18n.ts`
  (add EN **and** AR keys). Real-time hints arrive via `core/realtime.service.ts`.
- **Adding a screen**: create the standalone component under `web/src/app/features/...`, add a lazy route in
  `app.routes.ts`, add a nav entry in `layout/shell.component.ts` (gate it with a privilege), and add its
  nav i18n key in both dictionaries.
- **Windows dev note**: leftover `dotnet` host processes can lock DLLs during rebuild — kill them if a build
  fails to overwrite an assembly.

---

## 8. Where to look next

- [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md) — phase-by-phase status and everything that's been built.
- [docs/adr](docs/adr) — the 7 architecture decision records (modular monolith, single-tenant scope, SignalR,
  Hangfire, EF aggregate repos, `/api/v1` versioning, Angular 19).
- [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md) — open assumptions where the SRS was inferred.
- [docs/DEV-DATABASE.md](docs/DEV-DATABASE.md) — dev cluster setup, start/stop, migrations.
- The SRS package (`01-srs.html`, `02-workflows.html`, `03-architecture.html`, `design-system.html`) — the
  source of truth for requirements, workflows, and the design system.

Questions or something inaccurate here? Update this file in your PR — onboarding docs rot fast, and the next
person will thank you.
