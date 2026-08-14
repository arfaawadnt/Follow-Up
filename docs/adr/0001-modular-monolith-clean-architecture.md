# ADR-0001 — Modular monolith with Clean Architecture layers

**Status:** Accepted · 2026-08-15

## Context
The system spans 21 cohesive modules operated by one organization, deployed as a single instance (SRS).
The architect ruleset prefers a **modular monolith** unless independent deployment, ownership, scaling,
availability, or regulatory needs justify microservices — none of which apply here.

## Decision
Build a **modular monolith** with four Clean Architecture projects — `FollowUp.Domain`,
`FollowUp.Application`, `FollowUp.Infrastructure`, `FollowUp.Api` — dependency direction
Api → Infrastructure → Application → Domain (Infrastructure → Application & Domain). Modules are organized
as vertical feature folders inside Application (and matching Domain bounded-context folders), with explicit
consistency boundaries between them. Cross-module communication is in-process via MediatR notifications and
the Outbox, not shared tables.

## Alternatives considered
- **Microservices:** rejected — no independent scaling/ownership/deployment driver; would add distributed
  transactions, network failure modes, and operational cost the single-tenant single-instance product does
  not need.
- **Single-project layered app:** rejected — cannot mechanically enforce dependency direction; architecture
  tests need real project boundaries.

## Consequences
- Architecture tests assert the dependency direction and module boundaries in CI.
- A module could later be extracted to a service because boundaries and contracts are explicit.

## Risks
- Module boundaries erode over time. Mitigated by architecture tests and code review.

## Revisit criteria
A module needs independent scaling/deployment/ownership, or a regulatory boundary appears.
