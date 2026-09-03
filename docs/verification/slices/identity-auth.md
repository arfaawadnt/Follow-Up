# Slice register — Identity & Authorization core — audit cycle 2, 2026-08-27

Adversarial read-only audit. Rule quotes cite `architect-standard.txt` line numbers. Single-tenant (ADR-0002); "tenant isolation" reads as org-scope. Cross-refs: M-1, M-2, M-6, m-8 referenced not re-derived.

---

ID: IDN-1 · **Blocker** · VERIFIED
Rule: "Apply zero trust and defense in depth." (271); DoD "Domain behavior" (501), "Security tests" (517)
Location: Auth.cs:70-74 (`LoginHandler`) + TransactionBehavior.cs:36-51
Evidence: on bad password, `user.RegisterFailedLogin(...)` mutates the tracked `AppUser` **in memory**, then `throw new UnauthorizedException(...)`. `LoginCommand` is `IBaseCommand`, so TransactionBehavior only calls `SaveChangesAsync`/`CommitAsync` **after** `next()` returns — the throw skips both; the `await using` transaction rolls back. `DbUpdateConcurrencyException` is the only caught exception.
Mechanism: the failed-login increment is discarded on every attempt. `FailedLoginCount` stays 0, `LockedUntil` never set, `IsLockedOut` always false.
Impact: **per-account lockout (FR-1/NFR-SEC-4) is non-functional in production.** Only surviving brute-force control is the per-IP 10/min limiter — defeated with distributed IPs. Green unit tests (domain method + in-memory fake, no transaction) mask the dead production control — the "do not claim tests pass unless verified" hazard (453).
Fix: persist the counter outside the rolled-back command transaction — (a) commit the increment via a separate short transaction / `ExecuteUpdateAsync` before throwing; (b) return a typed failure result and map to 401 at the endpoint so TransactionBehavior commits; (c) dedicated repository call that saves independently. Files: Auth.cs, possibly IAppUserRepository/AppUserRepository.cs, + integration test that asserts the counter persists across separate requests.
Risk: option (b) changes the login exception flow — must keep the uniform "invalid username or password" (no user-enumeration regression); (a)/(c) must still run when verify throws and not itself roll back.

---

ID: IDN-2 · **Blocker** · VERIFIED (mechanism); trigger needs an Idempotency-Key on login
Rule: "Do not log: … Access tokens" (302-304); "Never place secrets in: … Logs / Plaintext production configuration" (295-300)
Location: IdempotencyBehavior.cs:46-54; response shape Auth.cs:13-21
Evidence: `ResponseJson = response is null or Unit ? null : JsonSerializer.Serialize(response)` — for `LoginCommand` (an `IBaseCommand`) this serializes the entire `LoginResult` including the raw bearer `Token` into `idempotency_record.response_json`, committed in the login transaction.
Mechanism: if any client/proxy/retry attaches an Idempotency-Key to `POST /auth/login`, the live token is stored in cleartext at rest — defeating the design where `UserSession` stores only a SHA-256 **hash** of the token (UserSession.cs:13-14).
Impact: live session credentials retrievable by anyone with DB read / a leaked backup, replayable until ~10h expiry. Secret exposure.
Fix: exclude anonymous/auth commands from idempotency response capture — don't persist `ResponseJson` for `LoginCommand` (a `[SensitiveResponse]` marker, or gate idempotency to authenticated non-auth commands). Files: IdempotencyBehavior.cs, optionally a marker on LoginResult.
Risk: a "don't store response for sensitive commands" rule must still allow legitimate replays for other commands — login should simply not be idempotency-cacheable rather than half-storing a record that replays `default!`.

---

ID: IDN-3 · Major · **INFERRED** (latent; no confirmed null-HttpContext caller in an HTTP scope)
Rule: "Apply zero trust and defense in depth." (271)
Location: CurrentUser.cs:34-48
Evidence: `IsBackground => _accessor.HttpContext is null; IsAuthenticated => IsBackground || State is not null; Privileges => State?.Privileges ?? (IsBackground ? Privileges.All : new HashSet<string>()); Scope => State?.Scope ?? (IsBackground ? OrgScope.Global : OrgScope.Deny); Has(p) => IsBackground || Privileges.Contains(p);`
Mechanism: the HTTP-registered `ICurrentUser` treats null `HttpContext` as "background principal" → **all privileges, global scope, Has()=>true**. A fail-**open** default inside the request-scoped HTTP implementation; any resolution off the request thread (fire-and-forget continuation outliving the request) becomes a full-privilege super-principal.
Impact: contradicts the fail-closed posture elsewhere (`OrgScope.Deny`, edge default-deny); latent privilege-escalation surface — authz defaults to "allow everything."
Fix: make HTTP `CurrentUser` fail-closed (unauthenticated + Deny + empty) when no state, regardless of HttpContext nullness; keep the full-privilege system principal only in `SystemCurrentUser` (already the TryAdd default for jobs/seed). Files: CurrentUser.cs.
Risk: any code (incorrectly) relying on the HTTP CurrentUser off-request would start getting 401/403 — verify jobs/seeding resolve SystemCurrentUser.

---

ID: IDN-4 · Major · VERIFIED
Rule: "Optimistic concurrency where required" (47); "Optimistic concurrency for conflicting updates" (211); DoD "Concurrency" (509)
Location: Role.cs:15, AppUser.cs:17 (neither IVersioned); IdentityConfigurations.cs (no token)
Evidence: `class Role : AggregateRoot<RoleId>, IAuditable` / `class AppUser : AggregateRoot<AppUserId>, IAuditable` — no IVersioned; grep `IVersioned|RowVersion` under Domain/Identity → none.
Mechanism: UpdateRole/UpdateUser/ChangeUserRole are last-writer-wins; TransactionBehavior's ConflictException path can never fire.
Impact: security-sensitive lost updates on privilege/scope grants — admin A tightens a role's privileges while admin B widens scope; one change vanishes with no 409.
Fix: add IVersioned (uint RowVersion→xmin) to Role and AppUser, map in IdentityConfigurations.cs, + concurrency test. Files: Role.cs, AppUser.cs, IdentityConfigurations.cs, migration, DomainModelTests ratchet, concurrency test.
Risk: xmin round-trip — role/user PUTs may need to carry the token, or the handler reloads-and-reapplies; verify the SPA "Save privileges" partial update still works.

---

ID: IDN-5 · Major · VERIFIED
Rule: "Session management" (280); "Token rotation and revocation" (281)
Location: UserCommands.cs:229-250 (`ChangeOwnPasswordHandler`)
Evidence: verifies old password, `user.SetPassword(...)`, `return Unit.Value` — **no session revocation**.
Mechanism: TokenAuthMiddleware authenticates purely on session validity (never re-checks the password), so every previously issued token stays valid to its ~10h expiry after a password change.
Impact: "I think I'm compromised, I changed my password" does not evict an attacker — a stolen bearer token survives the credential change for up to 10h. No admin "revoke all sessions" either.
Fix: on ChangeOwnPassword (and ideally admin role/deactivation changes) revoke the user's active sessions (`UPDATE user_session SET revoked_at=now WHERE user_id=… AND revoked_at IS NULL`), optionally preserving the current one. Files: UserCommands.cs, IUserSessionRepository/UserSessionRepository.cs (bulk revoke), test.
Risk: revoking the caller's own session forces immediate re-login — decide/document; run the revoke in the command transaction.

---

ID: IDN-6 · Minor · VERIFIED — the built-in admin is protected from **deletion** by a hardcoded `"admin"` literal (UserCommands.cs:185-188) but nothing blocks a `ManageUsers` holder from **demoting** it via UpdateUser/ChangeUserRole; invariant lives in a handler as a magic string, enforced inconsistently. Fix: `AppUser.IsBuiltIn` flag + one guard for delete AND role-change; remove the literal. Files: AppUser.cs, UserCommands.cs, seeder, migration. (55,181)

ID: IDN-7 · Minor · VERIFIED — app-level username uniqueness/lookup is case-insensitive (`ToLower`, IdentityRepositories.cs:15-18) but the DB unique index is case-sensitive (IdentityConfigurations.cs:18); `Admin` and `admin` can coexist, `GetByUsernameAsync` FirstOrDefaults ambiguously. Fix: `citext` or functional unique index on `lower(username)` + migration (existing case-variants need cleanup). (247)

ID: IDN-8 · Minor · VERIFIED — HmacTokenService.cs:52 (token expiry) and AdminQueries.cs:124 (session status) read `DateTimeOffset.UtcNow` directly instead of `IClock`, unlike the rest of the slice; untestable via FakeClock. Fix: inject IClock (both singletons — safe). (IClock convention; consistency)

ID: IDN-9 · Opinion · VERIFIED — `LookupUsersQuery` has `RequiredPrivileges = Array.Empty` (UserAdminQueries.cs:40-53): any authenticated caller enumerates up to 50 usernames — aids targeted credential attacks. Documented as non-credential attribution info (reasonable trade-off). Optional: gate behind ViewReps/ManageUsers. (no rule mandates username confidentiality → Opinion)

ID: IDN-10 · Opinion · VERIFIED — password policy is length-only (`MinimumLength(8)`, UserCommands.cs:43-51,224-227): no complexity/breach/rotation; no MFA anywhere. Acceptable if SRS scopes these out. Optional: complexity/deny-list validation; ADR if MFA deferred. (287,279 are topics, not verbatim rules → Opinion)

---

## DUPLICATION
- **Self-role-change guard duplicated** verbatim in UpdateUserHandler (UserCommands.cs:118-120) and ChangeUserRoleHandler (:156-158): `if (_caller.UserId == user.Id && new RoleId(request.RoleId) != user.RoleId) throw new ForbiddenException("You cannot change your own role.")`. Violates 57. Survivor: one shared guard (an `AppUser` domain method or a `RoleGrantSupport` helper) called from both.
- Anti-amplification (`EnsurePrivilegesWithinGrant`/`EnsureScopeWithinGrant`), grantable-role loading, and privilege expansion (`Privileges.Expand`) are already centralized — no duplication there.

## COVERAGE GAPS
- Org-scope isolation (users/roles/sessions): `SearchUsersAsync`/`GetRolesAsync`/session `GetAllAsync` apply no ScopeFilter and return network-wide rows to any ManageUsers holder (incl. other regions' IPs/UAs in AdminSessionDto). INFERRED concern for scoped sub-admins. **INSUFFICIENT EVIDENCE**: app_user/user_session carry no org dimensions — scope-filtering identity is undefined; need a product decision on whether a scoped sub-admin may see all identities. (269)
- Concurrency (509/446): no token on Role/AppUser (IDN-4); no competing-edit test.
- Idempotency (510/445): LoginCommand flows through IdempotencyBehavior and would store the token (IDN-2); no test asserts auth commands are excluded.
- Authorization/anti-amplification (445,439): create/update role + user grant paths covered by AntiAmplificationTests; UnlockUserHandler untested; no test that the built-in admin can't be demoted (IDN-6).
- Domain invariants/security (501,517): no integration test asserts FailedLoginCount **persists across separate failed requests** — the existing unit tests pass while production lockout is dead (IDN-1).
- Session lifecycle: no test that logout revocation is honored by the next request, nor that a password change evicts sessions (IDN-5).
- Validation: no NEW gaps — every command ships a validator or is pinned in the M-1 ratchet.

## DEFINITION OF DONE (lines 500-519)
Acceptance **NOT met** (FR-1 lockout dead, IDN-1) · Domain behavior Partial (lockout not persisted; self/admin guards in handlers not aggregate) · Architecture boundaries Met · CQRS Met · Validation Met (ratcheted) · Backend authz Met w/ caveat (fail-open fallback IDN-3) · Tenant/org isolation N/A-with-gap (identity intentionally global; scoped-sub-admin visibility unresolved) · DB constraints Partial (case-sensitive unique index vs case-insensitive rule IDN-7) · Indexes Met · Concurrency **NOT met** (IDN-4) · Idempotency Met for commands but login token leaks via store (IDN-2) · Auditing Met (MapAuditable on AppUser/Role; UserSession intentionally not audited) · Structured logging Met (passwords never logged) · Distributed tracing Met (OTel Program.cs:64-69) · Standard error handling Met · Unit tests Met but misleading for lockout (IDN-1) · Integration tests Partial (no lockout-persistence/concurrency/session-eviction) · Security tests Partial (no IDN-1/2/5 test) · Documentation Met (some XML-doc claims contradicted by IDN-1/2) · Deployment/config Partial (admin-password literal fallback = M-6).

## OBSERVED OUTSIDE SCOPE
- M-6: admin-password literal `"ChangeMe_Admin_2026!"` (Program.cs:78); `/esign/sign` re-auth (ServiceEndpoints.cs:41-42) not rate-limited — password-guessing oracle. (Also SIG-5/SIG-9.)
- m-8: Hangfire dashboard `LocalRequestsOnlyDashboardAuthorization` (Program.cs:150-153) loopback-only, not privilege-gated.
- M-2: GetLaboratories/GetLaboratoryById privilege gap (ratchet).
- TokenAuthMiddleware issues a synchronous `ExecuteUpdateAsync` (last-seen) on **every** authenticated request incl. GETs (TokenAuthMiddleware.cs:64-66) — write-on-read + 3 prior reads/request. Performance, not a standard violation.

## VERIFIED vs ASSUMED
VERIFIED (read): all Domain/Identity; Auth.cs; UserCommands/RoleCommands; UserAdminQueries; AntiAmplificationGuard; ScopeGuard; AuthorizationBehavior; Transaction/Idempotency/LoggingBehavior; HmacTokenService; Pbkdf2PasswordHasher; AuthOptions; SystemCurrentUser; CurrentUser; TokenAuthMiddleware; HttpIdempotencyKeyProvider; LocalRequestsOnlyDashboardAuthorization; Program.cs; AuthEndpoints; AdminEndpoints; IdentityRepositories; IdentityConfigurations; DatabaseSeeder; DependencyInjection (both); appsettings; Angular auth.service/guard/interceptor, users.component; tests AuthorizationTests/AuthorizationBehaviorTests/AntiAmplificationTests/AuthAndSignatureTests/SeedAndLoginTests/ContractTests/CqrsConventionTests.
VERIFIED correct-by-inspection: logout DOES revoke server-side + persist (LogoutCommand is IBaseCommand, mutates a tracked session, TransactionBehavior commits) — the asymmetry with the login-failure path is the root of IDN-1. TokenAuthMiddleware re-reads privileges/scope + checks revoke/expiry/token-hash each request. PBKDF2-SHA256/100k/constant-time. Token payload carries no secret/PII. Uniform login failure (no enumeration).
INFERRED: IDN-3 reachability (no confirmed null-HttpContext caller — latent). Org-scope applicability to identity lists.
INSUFFICIENT EVIDENCE: SRS FR-1/NFR-SEC-4 exact lockout wording (docs/SRS*) to confirm the intended behavior IDN-1 breaks.
