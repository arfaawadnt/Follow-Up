# ADR-0006 — API versioning under /api/v1

**Status:** Accepted · 2026-08-15

## Context
The reference SRS enumerates routes under `/api/...` (e.g. `/api/labs`, `/api/complaints/{id}/resolve`).
The architect ruleset requires explicit **API versioning** and shows `/api/v1/...` routes.

## Decision
Serve the API under **`/api/v1/`** using `Asp.Versioning` (URL-segment versioning), keeping the SRS's
resource paths and business-action sub-routes otherwise intact (e.g. `POST /api/v1/complaints/{id}/resolve`).
Health endpoints stay unversioned at `/healthz/*` (liveness/readiness/version) as infrastructure contracts.
The default-deny route policy table maps the versioned routes; the generated OpenAPI is the authoritative
contract.

## Alternatives considered
- **Unversioned `/api` (match SRS exactly):** simplest and route-for-route faithful, but forgoes the
  mandated evolution path; a breaking change later would have no clean migration.
- **Header/media-type versioning:** valid but less discoverable than URL segments for this audience.

## Consequences
- The Angular client targets `/api/v1`. The "116 route" contract is preserved semantically, prefixed by
  the version segment.

## Risks
- Minor divergence from the literal SRS paths. Low impact; documented here and reflected in OpenAPI.

## Revisit criteria
Introduction of a `v2` with breaking changes.
