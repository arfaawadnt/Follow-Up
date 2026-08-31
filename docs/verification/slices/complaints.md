# Slice register — Complaints (audit cycle 2, 2026-08-27)

Adversarial read-only audit. Standard quotes cite `architect-standard.txt` line numbers (extraction of
`Enterprise Application Architect.docx`). Tenancy framing per ADR-0002: org-scope is the isolation boundary.

---

ID:          CMP-1
Severity:    Blocker
Confidence:  VERIFIED
Rule:        "Enforce tenant isolation across: Commands / Queries" (lines 255-257); "Business rules and security enforcement must remain on the backend." (line 132)
Location:    src/FollowUp.Infrastructure/Persistence/Queries/ComplaintMarketingQueries.cs:68-100; src/FollowUp.Application/Features/Complaints/Queries/ComplaintQueries.cs:63-68, 83-84; src/FollowUp.Application/Features/Complaints/Contracts/ComplaintContracts.cs:26-27
Evidence:
```csharp
public async Task<ComplaintDetailDto?> GetByIdAsync(Guid id, bool canSeeEncrypted, CancellationToken ct)
{
    var row = await (from c in _db.Complaints.AsNoTracking()
                     where c.Id == new Domain.Complaints.ComplaintId(id)
                     join l in _db.Laboratories.AsNoTracking() on c.LaboratoryId equals l.Id
                     select new { c, l.Code, l.Name }).FirstOrDefaultAsync(ct);
...
public async Task<IReadOnlyList<ComplaintAuditRowDto>> GetAuditAsync(Guid id, CancellationToken ct)
{
    return await _db.AuditEntries.AsNoTracking()
        .Where(a => a.Entity == "Complaint" && a.EntityId == idStr)
```
Mechanism:   `SearchAsync` filters through `_db.Laboratories.ApplyScope(scope)`, but `GetByIdAsync` and `GetAuditAsync` take no `OrgScope` and apply none; their handlers add no `EnsureInScope`/`Scope.Allows` check. Contrast: every complaint command routes through `ComplaintActionSupport.LoadAuthorizedAsync` → `user.EnsureInScope(lab)`, and the labs module's own by-id read enforces scope.
Impact:      Any authenticated user holding ViewComplaints can read any complaint in the system by GUID — full details, lab name, investigation narrative, resolution — and its complete audit trail including before/after JSON snapshots, across org-scope boundaries. GUIDs leak via realtime hints, audit rows, logs, or shared links.
Fix:         Add `OrgScope scope` to `IComplaintQueries.GetByIdAsync`/`GetAuditAsync`; apply the `scopedLabs.Contains(c.LaboratoryId)` predicate in both (for audit, join complaint→lab first); pass `_user.Scope` from both handlers. Add an isolation test. Files: ComplaintContracts.cs, ComplaintQueries.cs, ComplaintMarketingQueries.cs, tests.
Risk of fix: Cross-scope deep links that "worked" return 404; audit read gains one indexed join. Global-scope users unaffected.

---

ID:          CMP-2
Severity:    Blocker
Confidence:  VERIFIED
Rule:        "Aggregate methods must protect invariants. Avoid public setters and uncontrolled collection mutation." (line 181); "Business rules and security enforcement must remain on the backend." (line 132); "Explicit state transitions" (line 43)
Location:    src/FollowUp.Domain/Complaints/Complaint.cs:96-124, 160-162; src/FollowUp.Application/Features/Complaints/Commands/ComplaintCommands.cs:173-195, 248-250; web/src/app/features/complaints/complaints.component.ts:161, 298-306
Evidence:
```csharp
// Complaint.cs:161
public void MoveToStage(ComplaintStage stage) => Stage = stage;
// ComplaintCommands.cs:249 (default branch of AdvanceComplaintStageHandler)
default:
    complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
```
```typescript
// complaints.component.ts:298-306 — the ONLY place stage order exists
stageForm(d: ComplaintDetail): StageForm {
    switch (d.stage) {
      case 'Logged': return 'ack';
      case 'Acknowledged': return 'validity'; ...
```
Mechanism:   (a) `MoveToStage` is a public setter in method clothing — any stage, any order, including terminal `Resolution`/`RejectedInvalid`, no guard; `ComplaintStage` has no transition map (unlike `ComplaintStatus`). (b) None of `CheckValidity`/`RecordInvestigation`/`RecordOutcome`/`MoveToStage` checks `Status`, so `/advance {stage:'Investigation'}` mutates a Resolved complaint's narrative (and, because Stage is in the signature hash, silently invalidates the e-signature bound to the resolved record while Status stays Resolved). (c) Privilege asymmetry: `POST /complaints/{id}/stage {stage:'Resolution'}` requires only UpdateComplaints, while real resolution requires ResolveComplaints + the e-sign gate — a user without resolve rights can make the stepper show a fully-resolved narrative. The workflow order and "no edits after resolve" exist only in the Angular component. This is the CMP-STAGE defect class reproduced one level down: status is gated, the staged workflow is not.
Impact:      Any UpdateComplaints holder can, via raw API calls, fake a complaint's investigation record (stage=Resolution without resolve privilege or signature), rewrite validity/investigation/outcome on closed complaints, or regress stages arbitrarily — corrupting the regulated FR-11 record and its e-signature semantics while every UI screen implies the workflow was followed.
Fix:         Give `ComplaintStage` an `Allowed` transition map + `EnsureCanTransitionTo` mirroring ComplaintStatus; make `MoveToStage` enforce it and reject `Resolution`/`RejectedInvalid` (reachable only via `Resolve`/`CheckValidity`); add `EnsureNotResolved()` to `CheckValidity`, `RecordInvestigation`, `RecordOutcome`, `MoveToStage`; remove or restrict the handler's raw default branch. Add domain tests. Files: ComplaintStage.cs, Complaint.cs, ComplaintCommands.cs, ComplaintTests.cs, ResolveComplaintHandlerTests.cs.
Risk of fix: Reopen → re-investigate needs a deliberate edge; the UI's Logged→Acknowledged path must stay legal; existing out-of-order rows will throw on next mutation — data review before deploy.

---

ID:          CMP-3
Severity:    Major
Confidence:  VERIFIED
Rule:        "Aggregate methods must protect invariants." (line 181); "A material record change must create a new version and require a new signature." (line 322)
Location:    src/FollowUp.Domain/Complaints/Complaint.cs:139-149
Evidence:
```csharp
public void Resolve(string actor, DateTimeOffset when, bool eSignatureSatisfied = true)
{
    Status.EnsureCanTransitionTo(ComplaintStatus.Resolved);
    if (!eSignatureSatisfied)
        throw new DomainException("Resolution requires a valid electronic signature...");
```
Mechanism:   The e-signature gate parameter defaults to `true` — fail-open. Any caller that omits the argument skips the gate silently; the domain's own tests already exercise the unsafe default (ComplaintTests.cs:34, 64).
Impact:      A future caller (new command, job, import) resolves complaints without the e-sign gate and nothing fails; the compliance control degrades to a convention.
Fix:         Remove the default (required parameter) or require a `SignatureEvidence` value object. Files: Complaint.cs, ComplaintTests.cs (:34, :64). ResolveComplaintHandler already passes explicitly.
Risk of fix: Compile-time breaks for omitting callers — that is the point.

---

ID:          CMP-4
Severity:    Major
Confidence:  VERIFIED
Rule:        "Content hash" (line 320); "A material record change must create a new version and require a new signature." (line 322)
Location:    src/FollowUp.Infrastructure/Gateways/ElectronicSignatureServices.cs:30-38
Evidence:
```csharp
// Canonical content: a change to any of these should invalidate an existing signature.
var canonical = string.Join("|",
    c.Number, c.Category, c.ViaChannel, c.AssignedTeam ?? "", c.Details, c.Status.Name, c.Stage.Name);
```
Mechanism:   The signable hash excludes `IsValid`, `ValidityNotes`, `InvestigationNotes`, `OutcomeType`, `OutcomeSummary`, `ResolutionSummary`, `RepresentativeId`, `ReceivedAt`. After signing, the investigation narrative can change without invalidating the signature (same-stage re-entry leaves `Stage.Name` unchanged). The resolve gate then accepts a stale signature attesting content it never covered.
Impact:      The e-signature attests less than the record shows; in a dispute the signed complaint's displayed outcome/validity may differ from what was signed — defeats FR-19 tamper-evidence.
Fix:         Add the narrative fields to `canonical` in RecordHasher + hasher unit test. All stored signatures then read invalid → operational note / migration for re-signing. Files: ElectronicSignatureServices.cs, new test.
Risk of fix: Every existing signature immediately reads "invalid — record changed"; coordinated rollout required.

---

ID:          CMP-5
Severity:    Major
Confidence:  VERIFIED
Rule:        "Duplicate business logic" (line 57); "Avoid duplicate implementations." (line 459)
Location:    src/FollowUp.Application/Features/Complaints/Commands/ComplaintCommands.cs:173-195 vs 201-254; src/FollowUp.Api/Endpoints/ServiceEndpoints.cs:33-36
Evidence:
```csharp
// MoveComplaintStageHandler (POST /complaints/{id}/stage)
complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
// AdvanceComplaintStageHandler default branch (POST /complaints/{id}/advance)
default:
    complaint.MoveToStage(Enumeration.FromName<ComplaintStage>(request.Stage));
```
Mechanism:   Two commands and two routes implement "move the stage", one validated, one not (MoveComplaintStageCommand is ratchet-pinned validator-less). Every CMP-2 hardening must be made twice or the unguarded twin remains a bypass.
Impact:      Divergent validation/authorization surfaces for one business action.
Fix:         Delete `MoveComplaintStageCommand`/handler + `/stage` route; point UI `acknowledge()` at `/advance {stage:'Acknowledged'}`; remove the ratchet entry. Files: ComplaintCommands.cs, ServiceEndpoints.cs, complaints.component.ts:402, CqrsConventionTests.cs.
Risk of fix: `/complaints/{id}/stage` is a published route — removing it unversioned is itself Blocker-class; keep it delegating to AdvanceComplaintStageCommand or retire via ADR-0006 versioning.

---

ID:          CMP-6
Severity:    Major
Confidence:  VERIFIED
Rule:        "Optimistic concurrency for conflicting updates" (line 211); "Record unavoidable exceptions as an ADR." (line 533)
Location:    src/FollowUp.Domain/Complaints/Complaint.cs:20 (not IVersioned); Migrations/FollowUpDbContextModelSnapshot.cs:236-364 (no token); src/FollowUp.Domain/Common/IAuditable.cs:15-18
Evidence:
```csharp
public sealed class Complaint : AggregateRoot<ComplaintId>, IAuditable   // not IVersioned
// IAuditable.cs:16-17 — the only record of the exception
/// Optimistic-concurrency marker. Labs and reps carry a row-version token; ... Other aggregates are last-writer-wins.
```
Mechanism:   Complaint is a multi-actor workflow record with no concurrency token — Resolve-vs-Reopen, Advance-vs-Resolve interleave last-writer-wins; TransactionBehavior's 409 mapping can never fire for complaints. Sign-gate TOCTOU: `HasValidSignatureAsync` hashes the row while a concurrent `/advance` commits changes between check and commit. The "last-writer-wins elsewhere" decision lives only in a code comment; no ADR covers it.
Impact:      Silent lost updates on the workflow (reopen can erase a concurrent resolution); resolution can commit against content the verified signature no longer matches.
Fix:         Implement `IVersioned` on Complaint + xmin mapping + migration; or write the ADR accepting the risk. Add a concurrency test. Files: Complaint.cs, MarketingComplaintConfigurations.cs, migration, docs/adr.
Risk of fix: Clients receive 409 on stale actions and must reload; xmin needs no data migration.

---

ID:          CMP-7
Severity:    Major
Confidence:  VERIFIED
Rule:        "Commands must be idempotent when they may be retried." (line 206); "Concurrency behavior" per API operation (line 335)
Location:    src/FollowUp.Infrastructure/Persistence/Repositories/OperationsRepositories.cs:70-71; ComplaintCommands.cs:73
Evidence:
```csharp
public async Task<int> NextNumberAsync(CancellationToken ct) =>
    (await _db.Complaints.MaxAsync(x => (int?)x.Number, ct) ?? 0) + 1;
```
Mechanism:   CMP-{n} counter is read-max-plus-one inside the transaction, no lock/sequence/retry. Two concurrent LogComplaintCommands compute the same Number; `ix_complaint_number` makes the loser throw Postgres 23505 as plain `DbUpdateException`, mapped by nothing → raw 500.
Impact:      Concurrent complaint intake produces 500s; the typed complaint is lost and must be re-entered.
Fix:         `pg_advisory_xact_lock` keyed on the counter inside NextNumberAsync (preserves gap-free BR-2; a sequence would leak gaps on rollback); also map 23505 → ConflictException in TransactionBehavior. Same fix for MarketingVisit counter (:59-60). Files: OperationsRepositories.cs, TransactionBehavior.cs, integration concurrency test.
Risk of fix: Serializes complaint creation (trivial rate); 23505→409 mapping is global — verify no flow depends on 500.

---

ID:          CMP-8
Severity:    Major
Confidence:  VERIFIED
Rule:        "Hardcoded permissions, configuration values, or UI text" (line 56); "Business rules and security enforcement must remain on the backend." (line 132)
Location:    web/src/app/features/complaints/complaints.component.ts:10-15; ComplaintCommands.cs:46-54, 208-221
Evidence:
```typescript
const CATEGORIES = ['Representative Issue', 'Call Center Issue', 'Result Quality', 'Data Entry Mistake'];
const CHANNELS = ['WhatsApp', 'Phone Call', 'Email', 'In-person'];
const OUTCOME_TYPES = ['Repeat Test / Service', 'Refund / Credit Note', 'Staff Training / Warning', ...];
```
Mechanism:   Category/channel/outcome vocabularies are hardcoded in the component while the sibling "teams" list is served from `/setup/refs`. Backend validates only NotEmpty; DB has no CHECK/FK on these columns. The strings exist nowhere in backend source.
Impact:      Any API caller stores arbitrary strings; category filter (exact match) and reporting fragment as soon as one variant lands; vocabulary changes require a frontend release.
Fix:         Seed RefItem types (ComplaintCategory, ComplaintChannel, OutcomeType); load in component like teams; validate membership in handlers. Files: Seeding, complaints.component.ts, ComplaintCommands.cs, tests.
Risk of fix: Existing non-matching rows break filters — backfill first; previously-accepted free text now rejected.

---

ID:          CMP-9
Severity:    Major
Confidence:  VERIFIED
Rule:        "Angular components must not contain business rules or direct persistence orchestration." (line 367); Application contains "Use-case orchestration" (line 96)
Location:    web/src/app/features/complaints/complaints.component.ts:401-405, 416-421
Evidence:
```typescript
acknowledge(d: ComplaintDetail): void {
    const moveStage = () => this.act(d.id, this.api.post(`/complaints/${d.id}/stage`, { stage: 'Acknowledged' }));
    if (d.status === 'Open') this.api.post(`/complaints/${d.id}/start`).subscribe({ next: moveStage, error: moveStage });
    else moveStage();
}
...
const summary = d.stage === 'RejectedInvalid' ? `Invalid Complaint${notes ? ': ' + notes : ''}` : (notes || null);
```
Mechanism:   (a) "Acknowledge" is a composite use case (Open→InProgress AND stage→Acknowledged) sequenced client-side with `/start` failure swallowed (`error: moveStage`) — a failed status change still advances the stage; no transaction. (b) The "invalid complaint closes with an 'Invalid Complaint' summary" rule is string-fabricated client-side; the backend accepts any summary.
Impact:      Non-UI callers or error paths produce half-applied workflow steps and invalid complaints closed without the mandated marker.
Fix:         AcknowledgeComplaintCommand (or extend the Acknowledged case) doing both in one transaction; move the invalid-closure summary rule into Complaint.Resolve/handler keyed off `Stage == RejectedInvalid`. Files: ComplaintCommands.cs, ServiceEndpoints.cs, complaints.component.ts, handler tests.
Risk of fix: Contract additions if old routes kept; summary-composition move changes stored text for API clients that sent their own.

---

ID:          CMP-10
Severity:    Major
Confidence:  VERIFIED
Rule:        "Unsafe cascading deletes" (line 243); "Use database constraints as a second line of defense for business integrity." (line 247)
Location:    src/FollowUp.Infrastructure/Persistence/Configurations/MarketingComplaintConfigurations.cs:68
Evidence:
```csharp
b.HasOne<Laboratory>().WithMany().HasForeignKey(x => x.LaboratoryId).OnDelete(DeleteBehavior.Cascade);
```
Mechanism:   Deleting a laboratory cascades deletion of its complaints — regulated FR-11 records. No application path deletes laboratories today (grep: zero hits), so the vector is ops SQL or a future feature — which would silently destroy complaint history.
Impact:      Latent data-loss vector at schema level.
Fix:         `DeleteBehavior.Restrict` + migration altering the FK. (MarketingVisit line 30 has the same cascade — out of slice, noted below.) Files: MarketingComplaintConfigurations.cs, migration.
Risk of fix: Future lab-deletion must handle complaints explicitly — intended friction.

---

ID:          CMP-11 · Minor · VERIFIED — `complaint.stage` has no CHECK constraint though SchemaHardening promises one per enumeration and `status` got one. Fix: migration adding `ck_complaint_stage`. Risk: fails if invalid rows exist — validate first. (lines 247; SchemaHardening.cs:17-33)

ID:          CMP-12 · Minor · VERIFIED — `complaint.representative_id` is an unconstrained Guid: no FK, no existence/scope check in LogComplaintHandler; detail read resolves any rep's name by GUID (marginal cross-scope name disclosure). Fix: FK (Restrict/SetNull) + migration + handler existence check. (lines 229, 247)

ID:          CMP-13 · Minor · VERIFIED — `POST /complaints` binds LogComplaintCommand directly as the wire body (every sibling route uses a dedicated body record) and `Results.Created` points at the collection URI without the new id. Fix: LogComplaintBody + `{id, reference}` + resource URI. Risk: response-shape change — keep `reference`. (lines 327-328, 117)

ID:          CMP-14 · Minor · VERIFIED — stage names are raw string literals repeated in validator + handler switch + Angular STEPS; rename/typo compiles clean and falls into the unvalidated default branch. Fix: compare against `ComplaintStage.X.Name`. (line 55)

ID:          CMP-15 · Minor · VERIFIED — `GetComplaintsQuery.Search` and inherited SortBy/SortDescending are accepted and silently ignored by SearchAsync; paging re-declared instead of ListQuery. Fix: implement or remove. (lines 338-339)

ID:          CMP-16 · Minor · VERIFIED — component fetches fixed pageSize=100, ignores `PagedResult.Truncated`; status-pill counts computed client-side from the loaded page (wrong past 100 or under filters). Fix: honor truncated/total, backend counts. (lines 338, 368-370)

ID:          CMP-17 · Minor · VERIFIED — modals are plain divs (no role/aria-modal/focus trap/Escape), filter pills are click-only spans — workflow not keyboard/screen-reader operable. Fix: native button/dialog semantics. (line 378)

ID:          CMP-18 · Minor · VERIFIED — ComplaintConfiguration hand-inlines the four audit-column mappings that `MapAuditable()` owns everywhere else (11+ call sites); helper changes silently skip complaint. Fix: `b.MapAuditable();` — no migration (verify with a no-op drift check). (line 459)

ID:          CMP-19 · Minor · VERIFIED — existing validators omit bounds the schema enforces (varchar 100/2000): over-length input → DbUpdateException 22001 → 500 instead of 400; `ReceivedAt` accepts future dates. Fix: MaximumLength mirrors config + ReceivedAt sanity rule + validator tests. (lines 38, 329)

ID:          CMP-20 · Opinion · VERIFIED behavior / INFERRED intent — Reopen clears resolver fields but leaves `Stage = Resolution` + ResolutionSummary: reopened complaints can only be re-resolved (workflow dead-end); stale resolution text shows on an open complaint's detail. Domain-owner decision: reopen → Stage=Investigation + clear/version summary.

ID:          CMP-21 · Opinion · VERIFIED behavior / INFERRED intent — invalid verdict sets Stage=RejectedInvalid but Status stays Open/InProgress; unattended invalid complaints inflate Open KPIs until someone resolves (with e-sign) a complaint already judged invalid. Domain-owner decision: auto-resolve on invalid (modeled explicitly) or a RejectedInvalid status.

---

## DUPLICATION (Complaints)

1. MoveComplaintStageCommand + `/stage` vs AdvanceComplaintStageCommand default branch + `/advance` — same operation, one validated. **Advance survives**; `/stage` retired via versioning (CMP-5).
2. `NextNumberAsync` max+1 racy counter: ComplaintRepository (:70-71) and MarketingVisitRepository (:59-60). **One advisory-locked helper survives** (CMP-7).
3. Audit-column mapping: ComplaintConfiguration:63-66 vs `MapAuditable()` (ConfigurationExtensions.cs:14-20). **MapAuditable survives** (CMP-18).
4. `"CMP-{n}"` reference format: Complaint.Reference vs projection literal `$"CMP-{r.Number}"` vs UI hint. **Domain format survives**; shared const for the projection.
5. Semantic: stage vocabulary triplicated (Domain enumeration / Application string literals / Angular STEPS). **Domain enumeration survives.**
6. Semantic: complaint taxonomy hardcoded client-side while `/setup/refs` ref-data mechanism exists (used for Teams in the same form). **Ref-data mechanism survives** (CMP-8).
7. GetComplaintsQuery paging/search re-declaration vs ListQuery. **ListQuery survives** (CMP-15).

## COVERAGE GAPS (Complaints)

- Org-scope isolation: none (would have caught CMP-1). Standard line 269.
- Authorization: none (no privilege-denial test on any of the 8 requests; none for the stage/resolve asymmetry).
- Idempotency: none; the web client never sends Idempotency-Key (grep: zero hits) — the behavior is dead weight for this slice.
- Concurrency: none (CMP-6/CMP-7 untested).
- Validation: AdvanceComplaintStageValidator has one test; LogComplaintValidator untested; 4 commands have no validators (ratchet).
- Domain invariants: status machine well covered; stage ordering untestable (no rule exists — CMP-2); `MoveToStage_never_changes_status` test pins the unguarded setter as intended.
- Persistence: no EF round-trip test for Complaint.
- Contract: zero ApiTests mention Complaint.
Covered: domain status machine, resolve/advance handler happy paths + e-sign gate on/off, outbox fan-out of ComplaintLogged.

## DEFINITION OF DONE (Complaints) — standard lines 500-519

| Item | Verdict | Evidence |
|---|---|---|
| Acceptance criteria | INSUFFICIENT EVIDENCE | SRS not supplied to auditor (FR-11/BR-2/BR-11 cited in comments) |
| Domain behavior | Not met | stage = unguarded setter; Resolve fail-open (CMP-2/3) |
| Architecture boundaries | Met | Domain imports only FollowUp.Domain.*; handlers in Application; SQL in Infrastructure |
| CQRS separation | Met | commands vs AsNoTracking projections; no aggregate loads on list paths |
| Validation | Not met | 4 validator-less commands (ratchet); bounds missing (CMP-19) |
| Backend authorization | Not met | privileges declared on all 8 requests, but by-id reads unscoped (CMP-1); stage path undercuts resolve privilege (CMP-2) |
| Tenant isolation | Not met | list scoped; by-id/audit unscoped (CMP-1); no isolation tests |
| Database constraints | Not met | stage CHECK missing (CMP-11); rep FK missing (CMP-12); unsafe cascade (CMP-10) |
| Indexes | Met | ix_complaint_laboratory_id, ix_complaint_number (unique), ix_complaint_status match read paths |
| Concurrency | Not met | no token; comment-only exception, no ADR (CMP-6); counter race (CMP-7) |
| Idempotency | Not met | key optional, never sent by client, untested |
| Auditing | Met (caveat) | audit trail queryable + DB-immutable (SchemaHardening.cs:38-64); writing interceptor INFERRED from inventory |
| Structured logging | INFERRED Met | shared LoggingBehavior per inventory; file not opened by this slice audit |
| Distributed tracing | INSUFFICIENT EVIDENCE | needs full Program.cs OTel wiring review |
| Standard error handling | Met (exception) | single ProblemDetails mapper; unique-violation → 500 (CMP-7) |
| Unit tests | Partial | domain + resolve/advance handlers tested; Log/Start/Reopen/MoveStage + LogComplaintValidator untested |
| Integration tests | Not met | only outbox fan-out + one KPI zero-check touch complaints |
| Security tests | Not met | zero authorization/isolation tests |
| Documentation | INSUFFICIENT EVIDENCE | module-level docs expectations unclear |
| Deployment/config | INSUFFICIENT EVIDENCE | `esign.enforce.complaint` seeding not confirmed (DatabaseSeeder.cs not read by this audit) |

## OBSERVED OUTSIDE SCOPE

- MarketingVisit shares the cascade-delete (MarketingComplaintConfigurations.cs:30) and max+1 counter race (OperationsRepositories.cs:59-60).
- `SignBody` carries a plaintext `Password` through `POST /esign/sign` — signature-slice concern (see SIG register).
- RecordHasher hardcodes module string "complaint" (ElectronicSignatureServices.cs:23) — brittle for the next signable module.
- List AgeDays uses `DateTime.UtcNow` — already registered M-4; manifestation now at ComplaintMarketingQueries.cs:58,62.

## VERIFIED vs ASSUMED

VERIFIED by reading: all Domain/Complaints files; ComplaintCommands/Queries/Contracts; ComplaintMarketingQueries; ComplaintRepository; configurations + SchemaHardening + snapshot DDL; ServiceEndpoints (9 routes); ElectronicSignatureServices (gate+hasher); AuthorizationBehavior; ScopeGuard.EnsureInScope; PagedResult/ListQuery; ExceptionHandlingMiddleware; TransactionBehavior; complaints.component.ts in full; both complaint test files; ADR-0002; DI registrations.
INFERRED: AuditAndOutboxInterceptor writes the rows GetAuditAsync reads; LoggingBehavior/OTel coverage; IdempotencyBehavior internals.
INSUFFICIENT EVIDENCE: the SRS itself; full Program.cs; DatabaseSeeder.cs; LoggingBehavior.cs.
