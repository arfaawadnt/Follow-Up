# Follow-Up Management System

A single-tenant, bilingual (EN/AR, RTL) B2B client-relations & field-operations platform for a medical-lab
group. Built to the **Enterprise Application Architect** ruleset: **Clean Architecture + SOLID + DDD + CQRS**.
Reconstructed from the SRS package (`index.html`, `01-srs.html`, `02-workflows.html`, `03-architecture.html`,
`design-system.html`) — 21 modules · 31 tables · 116 routes · 3-layer authorization.

## Stack
- **Backend:** .NET 8 · ASP.NET Core · EF Core · PostgreSQL 17 · MediatR (CQRS) · FluentValidation · Serilog ·
  OpenTelemetry · SignalR · Hangfire
- **Frontend:** Angular 19 (ADR-0007; forward-compatible with 22) · standalone · signals · typed reactive forms
- **Served on port 5088** (5080 is reserved on the target host).

## Solution layout
```
src/
  FollowUp.Domain          aggregates, value objects, enums (state machines), domain events — zero deps
  FollowUp.Application     CQRS commands/queries, validators, abstractions, pipeline behaviors
  FollowUp.Infrastructure  EF Core, repositories, gateways, Hangfire jobs, auth, outbox/audit interceptor
  FollowUp.Api             middleware pipeline, /api/v1 endpoints, SignalR hub, SPA hosting
web/                       Angular SPA (built into the API wwwroot)
tests/                     Domain, Application, Integration (live DB), Architecture (dependency direction)
docs/                      BUILD-PLAN, ASSUMPTIONS, ADRs
```
Dependency direction (CI-gated by architecture tests): **Api → Infrastructure → Application → Domain**.

## Run in development
Requires .NET 8 SDK, Node 20, and a PostgreSQL 17. Set the connection + a signing secret via env:
```bash
export FOLLOWUP_DB="Host=127.0.0.1;Port=5442;Database=followup;Username=postgres;Password=<pw>"
export FOLLOWUP_AUTH_SECRET="<a-strong-secret>"
# API (applies migrations + seeds on startup): serves on http://localhost:5088
dotnet run --project src/FollowUp.Api
# SPA with live reload + proxy to the API:
cd web && npm ci && npx ng serve   # http://localhost:4200
```
The seeder creates an `admin` login (password from `FOLLOWUP_ADMIN_PASSWORD`, default `ChangeMe_Admin_2026!`).

## Run with Docker
```bash
export FOLLOWUP_AUTH_SECRET="<a-strong-secret>"
docker compose up --build      # app on http://localhost:5088, its own PostgreSQL 17
```
The image is multi-stage (Angular build → .NET publish → runtime), runs as a non-root user, and the app
self-provisions the schema on startup.

## Tests
```bash
dotnet test FollowUp.sln       # domain, application, integration (needs FOLLOWUP_DB), architecture
cd web && npx ng test          # Angular unit tests (headless Chrome)
```

## Reference-build corrections applied
JOBS-001 (evening missed-sweep runs before archive), JOBS-002 (scheduled Oracle sync audited),
JOBS-003 (HTML email escaped), JOBS-006 (auto notification retry), SCOPE-READ (scoped reads),
CMP-STAGE (status only via the state machine), ESIGN-UI (signing flow provided).

See [docs/BUILD-PLAN.md](docs/BUILD-PLAN.md) for phase status and [docs/adr](docs/adr) for decisions.
