# ADR-0013 — Authentication token: HMAC bearer, fixed lifetime, and client storage

**Status:** Accepted · 2026-09-09 (records existing behaviour — findings IAM-008, PLT-015)

## Context
Authentication issues a bearer token on login (`HmacTokenService`). The token is an HMAC-signed reference to a
server-side `UserSession` row; it carries the session id but **no privileges or scope** — every request
re-reads the user's role → privileges + org-scope from the database (`TokenAuthMiddleware`, NFR-SEC-2), so a
role/scope change or a revoked session takes effect immediately. Sessions have a fixed lifetime
(`Auth:TokenLifetimeHours`, default 10h) and can be revoked (`UserSession.Revoke`, enforced by
`IsActive(now)`); a password change revokes the user's other sessions (IDN-5). The token has **no refresh or
rotation** — when it expires the user logs in again. The Angular client stores the login result (including the
token) in `localStorage` (`auth.service.ts`). Two adversarial-audit *opinions* asked that these deliberate
choices be recorded: IAM-008 (no rotation/refresh, undocumented) and PLT-015 (bearer in `localStorage`).

## Decision
Keep the current model and document it:

- **Opaque, DB-backed HMAC bearer token.** Stateless verification of the signature + a session lookup; no
  claims trusted from the token. Revocation and immediate privilege/scope re-evaluation come for free.
- **Fixed 10h lifetime, no refresh/rotation.** A single short-lived session per login; re-authenticate on
  expiry. Simpler than a refresh-token dance, and adequate for an internal, single-tenant operations tool.
- **Client stores the token in `localStorage`.** The app is a same-origin SPA served by the API host under a
  strict CSP (`script-src 'self'`, `frame-ancestors 'none'`) with `X-Content-Type-Options`/`X-Frame-Options`;
  the token is sent as an `Authorization: Bearer` header (not a cookie), which sidesteps CSRF.

## Alternatives considered
- **Refresh + rotation (short access token, longer refresh token).** Standard for public/mobile clients and
  reduces the window of a leaked access token, but adds a refresh endpoint, rotation/replay handling, and
  storage of a second secret — disproportionate for a 10h internal session that is already revocable.
- **`httpOnly`, `Secure`, `SameSite` cookie instead of `localStorage`.** Immune to token theft via XSS-read,
  but reintroduces CSRF (needing anti-forgery tokens) and complicates the SignalR hub handshake (which already
  passes the token via the query string, IAM-010). The XSS surface is mitigated by the strict CSP and the
  fact that the API and SPA are same-origin with no third-party script.

## Consequences
- Token theft (e.g. via a hypothetical XSS despite the CSP) grants access until the session expires or is
  revoked; there is no automatic rotation to shorten that window.
- No silent session extension — an operator is logged out after 10h and must sign in again.
- Revocation, per-request re-authorization, and password-change session eviction are already in place.

## Risks
- `localStorage` is readable by any script on the origin, so the CSP is load-bearing for token confidentiality.

## Revisit criteria
Exposure of the app to untrusted networks or third-party scripts; a requirement for long-lived sessions or
"remember me"; or a move to a cookie-based session — any of these should trigger refresh/rotation and/or an
`httpOnly` cookie with CSRF protection.
