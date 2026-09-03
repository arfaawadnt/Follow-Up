# ADR-0002 — Single-tenant deployment; org-scope as the isolation boundary

**Status:** Accepted · 2026-08-15

## Context
The Enterprise Application Architect ruleset treats multi-tenancy as a default concern (tenant resolution
from trusted identity, RLS evaluation, tenant-isolation tests across commands/queries/jobs/SignalR/etc.).
The Follow-Up SRS, however, is explicit and repeated: the system is **single-tenant** — one medical-lab
group ("Mega Laboratory"), single instance, port 5080, in-memory real-time state, horizontal scale-out
out of baseline. There is no second customer to isolate from.

This is a direct conflict between the architect's default and the product requirements.

## Decision
Build the system **single-tenant**. The architect's "tenant isolation" objective is satisfied by the
SRS's first-class **six-dimension organizational scope + record-ownership** model (branches, governorates,
cities, areas, categories, segments), enforced server-side on every command, query, background job and
real-time subscription. That scope — not a tenant id — is the access-isolation boundary and is what the
isolation tests target. No `tenant_id` column is added; adding one later is non-breaking.

## Alternatives considered
- **Shared-schema multi-tenant (tenant_id everywhere):** rejected — models a dimension the business does
  not have; adds cost/complexity and dead columns; contradicts the SRS.
- **Database-per-tenant:** rejected for the same reason, more strongly.
- **PostgreSQL RLS for tenancy:** not applicable with one tenant. RLS is still considered as a *defense in
  depth* option for the org-scope filter (documented in the security design), not for tenancy.

## Consequences
- The scope evaluator (`OrgScope`) is the security-critical isolation unit; it gets exhaustive tests.
- Trusted-source rule still honored: privileges and all six scope arrays are re-read from the DB every
  request, never trusted from the token.
- Application/handler code is written scope-aware so a future tenant dimension could be layered on.

## Risks
- If the business later onboards a second isolated group, a tenancy migration is required. Mitigated by
  keeping scope enforcement centralized so a tenant dimension can be added in one place.

## Revisit criteria
A second independent customer/group, a regulatory data-residency split, or a hosting model change.
