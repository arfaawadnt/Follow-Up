# ADR-0003 — SignalR for real-time (replaces reference SSE + tickets)

**Status:** Accepted · 2026-08-15

## Context
The reference implementation pushes real-time board/notification updates over **Server-Sent Events**,
credentialed by single-use 30-second stream tickets (because a bearer token must never sit in a URL), with
an in-memory subscriber registry and 15-second heartbeats. The architect ruleset names **SignalR** as the
standard real-time technology and prescribes how to secure it.

## Decision
Use **SignalR** for real-time server→client hints (data-change signals and in-app notifications). Apply the
architect's SignalR rules: authenticate every connection (bearer via the negotiate step / access-token
factory, not a URL), authorize every group subscription server-side, scope groups by resource/scope, never
trust client-provided group names, keep payloads minimal (entity type + ids), and treat messages as
**hints** — the client re-fetches through the normal scope-enforced query path, which remains the system of
record. Reconnect/missed-event handling via client re-fetch on connect.

## Alternatives considered
- **Keep SSE + tickets:** faithful to the reference and simpler, but diverges from the mandated stack and
  reimplements connection management SignalR already solves (reconnect, backpressure, transports).
- **WebSocket by hand:** rejected — more work, fewer safety rails than SignalR.

## Consequences
- The SRS notification-stream endpoints (`/stream-ticket`, `/stream`) are replaced by a SignalR hub with an
  authenticated negotiate. The security property (no token in URL) is preserved by SignalR's access-token
  mechanism.
- Baseline stays single-instance (in-memory groups). A backplane (Redis) is the documented scale-out path.
- OpenTelemetry instruments hub operations.

## Risks
- SignalR fallback transports must still satisfy the CSP. Mitigated by configuring allowed transports and
  same-origin hub.

## Revisit criteria
A need to scale beyond one instance (add Redis backplane) or a client that cannot use SignalR.
