# ADR-0014 — The system (background) principal runs with full authority

**Status:** Accepted · 2026-09-09 (records existing behaviour — finding PLT-016)

## Context
`ICurrentUser` resolves per request. On an HTTP request it reflects the authenticated user's privileges and
org-scope; when there is **no `HttpContext`** — background jobs, the outbox dispatcher, startup seeding — it
falls back to a *system principal*: `Privileges => Privileges.All`, `Scope => OrgScope.Global`
(`CurrentUser`/`SystemCurrentUser`). An adversarial-audit *opinion* (PLT-016) flagged this as broad ambient
authority and asked that it be recorded as a deliberate choice.

## Decision
Background/scheduled work runs as a full-authority system principal, and this is intentional:

- **There is no user to scope to.** A recurring job (board roll-over, Oracle sync, retention, outbox drain,
  segment assignment, notification retry) acts on behalf of the whole system, across every branch/governorate
  — the operations it performs are inherently global (reconcile all labs, purge across all tenants-of-one,
  dispatch every pending outbox message). Constraining it to a narrower scope would be incorrect, not safer.
- **The authority is not reachable by a user.** The system principal only arises when `HttpContext` is null.
  Every HTTP entrypoint carries a real authenticated principal (the edge auth gate rejects unauthenticated
  `/api/v1` requests), so a user can never *borrow* system authority — the two code paths are disjoint.
- **The surface is a fixed, reviewed set.** System-authority code is exactly the recurring jobs and their
  services (ADR-0004) plus first-run seeding — not open-ended. Those services are individually tested.

## Alternatives considered
- **A dedicated, scoped "system" role seeded in the DB.** More explicit, but a job legitimately needs global
  reach, so the role would carry `Privileges.All`/`OrgScope.Global` anyway — the same authority with more
  moving parts and a role row that must be protected from edit/deletion.
- **Per-job least-privilege principals.** Each job declares only the privileges it uses. Meaningful in a
  multi-tenant or multi-actor system; here every job is a trusted, first-party, whole-system operation, so the
  bookkeeping cost outweighs the benefit.

## Consequences
- Background code is not constrained by the org-scope/privilege checks that guard user requests; correctness
  of what a job may touch rests on the job's own logic and its tests, not on `ICurrentUser`.
- The audit trail records the system actor (via the `SaveChanges` interceptor) for job-driven mutations, so
  system actions remain attributable even though they are unconstrained.

## Risks
- A bug in a job runs with full authority. Mitigated by: the disjoint HTTP-vs-background code paths (no user
  escalation), the fixed job set, per-job tests, and audited mutations.

## Revisit criteria
Multi-instance/multi-actor background execution, third-party or operator-authored jobs, or any path where a
user request can trigger system-authority code — any of these should move jobs to explicit least-privilege
principals.
