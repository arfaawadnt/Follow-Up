# ADR-0007 — Angular 19 (Node runtime constraint)

**Status:** Accepted · 2026-08-15

## Context
The Enterprise Application Architect ruleset specifies **Angular 22** (with strict TypeScript, standalone
components, signals, RxJS, typed reactive forms, feature-based architecture). Angular CLI 22 requires
Node `^22.22.3 || ^24.15.0 || >=26`. The build host runs **Node 20.18.1**, which cannot run Angular 20+.
The latest Angular that runs on Node 20.18.1 is **Angular 19** (engines `^20.11.1`), and it provides every
Angular capability the ruleset requires — the difference from 22 is the version number, not the architecture.

## Decision
Build the frontend on **Angular 19** with strict TypeScript, standalone components, signals, typed reactive
forms, lazy-loaded feature routes, centralized API services and feature stores — matching the ruleset's
Angular requirements exactly. The `package.json` engines are documented so a Node 22 upgrade unlocks a clean
bump to Angular 22 (`ng update @angular/core@22 @angular/cli@22`) with no architectural change.

## Alternatives considered
- **Install/upgrade the host Node to 22+:** would allow Angular 22, but modifies a shared server that other
  running services (e.g. `nt-qams`) may depend on — an out-of-scope system change with blast radius.
- **Portable Node 22 side-load (download zip):** avoids a system change but requires downloading and executing
  an unsanctioned binary; deferred unless the user requests it.
- **Angular 22 anyway:** not possible — the CLI refuses to run on Node 20.18.1.

## Consequences
- Frontend targets Angular 19; the code style is forward-compatible with 22.
- CI/dev docs note the Node requirement; upgrading Node is the single step to reach Angular 22.

## Risks
- Minor version lag vs the stated stack. Low impact — the mandated patterns are all present in 19.

## Revisit criteria
Host Node upgraded to ≥22, or the user opts to side-load a portable Node 22.
