# Architecture Decision Records

Significant decisions for the Follow-Up Management System rebuild, per the Enterprise Application
Architect ruleset. Each ADR: Context · Decision · Alternatives · Consequences · Risks · Revisit criteria.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-modular-monolith-clean-architecture.md) | Modular monolith with Clean Architecture layers | Accepted |
| [0002](0002-single-tenant-with-org-scope-isolation.md) | Single-tenant deployment; org-scope as the isolation boundary | Accepted |
| [0003](0003-signalr-for-realtime.md) | SignalR for real-time (replaces reference SSE + tickets) | Accepted |
| [0004](0004-hangfire-for-background-jobs.md) | Hangfire for background jobs (replaces advisory-locked hosted services) | Accepted |
| [0005](0005-ef-core-aggregate-repositories.md) | EF Core: aggregate repositories + DTO-projecting read services | Accepted |
| [0006](0006-api-versioning.md) | API versioning under /api/v1 | Accepted |
| [0007](0007-angular-19-node-constraint.md) | Angular 19 (Node runtime constraint) | Accepted |
