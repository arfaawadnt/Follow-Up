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
| [0008](0008-esign-hash-hard-cutover.md) | Electronic-signature content-hash correction (hard cutover) | Accepted |
| [0009](0009-single-commission-formula.md) | Commission uses one sample-count formula for all rep types | Accepted |
| [0010](0010-esignature-intent-subsumed-by-meaning.md) | An electronic signature's Intent is realized through Meaning + the signing ceremony, not a separate field | Accepted |
| [0011](0011-password-policy-and-mfa-deferral.md) | Password policy: length + complexity + deny-list; MFA deferred | Accepted |
| [0012](0012-commands-as-http-wire-contracts.md) | MediatR commands may serve as HTTP wire contracts for internal endpoints | Accepted |
| [0013](0013-auth-token-lifecycle-and-storage.md) | Authentication token: HMAC bearer, fixed lifetime, and client storage | Accepted |
| [0014](0014-system-principal-authority.md) | The system (background) principal runs with full authority | Accepted |
| [0015](0015-suppress-sync-derived-domain-events.md) | Suppress domain events during bulk sync re-derivation | Accepted |
