# ADR-0005 — EF Core: aggregate repositories + DTO-projecting read services

**Status:** Accepted · 2026-08-15

## Context
The user chose EF Core + the repository pattern. The architect ruleset forbids **generic repositories,
repository-per-table abstractions, exposed IQueryable, and redundant Unit-of-Work abstractions over
DbContext**, and requires that queries project directly to DTOs without loading full aggregates.

## Decision
- **Write side:** one **aggregate-oriented repository interface per aggregate root** (e.g.
  `ILaboratoryRepository`, `IComplaintRepository`), defined in Application, implemented in Infrastructure.
  They return/accept domain aggregates only — no `IRepository<T>`, no per-table repositories, no exposed
  `IQueryable`. Loading an aggregate loads its whole consistency boundary.
- **Transactions / UoW:** the EF Core `DbContext` **is** the unit of work. No `IUnitOfWork` wrapper in
  Application. A MediatR `TransactionBehavior` in Infrastructure opens a transaction around command
  handling, calls `SaveChanges`, dispatches domain events via the **Outbox**, and commits.
- **Read side (CQRS queries):** query handlers depend on narrow, purpose-built **read interfaces** defined
  in Application (e.g. `ILaboratoryQueries.SearchAsync(...) : PagedResult<LabListItemDto>`), implemented in
  Infrastructure with EF `Select` projections straight to DTOs. No aggregate hydration, no `IQueryable`
  crossing the layer boundary. Scope filters are pushed into SQL with an in-memory post-check (SRS NFR-PERF-3).

## Alternatives considered
- **Generic `IRepository<T>` + `IUnitOfWork`:** explicitly forbidden; leaks persistence concerns and
  encourages anemic use.
- **Dapper for reads:** viable and fast, but EF projections keep one data stack and satisfy the requirement;
  Dapper remains an option for hot report queries if profiling demands it.

## Consequences
- More small, intentional interfaces, but each expresses a real use case and keeps layers honest.
- Reads never over-fetch; list/report endpoints stay within the SRS performance envelope.

## Risks
- Projection duplication across similar queries. Mitigated with shared projection expressions where sensible.

## Revisit criteria
Profiling shows EF read overhead on specific reports → selectively introduce Dapper behind the same read
interface.
