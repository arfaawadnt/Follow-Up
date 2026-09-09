# ADR-0015 — Suppress domain events during bulk sync re-derivation

**Status:** Accepted · 2026-09-09 (records existing behaviour — finding STAT-013)

## Context
A lab's lifecycle `Status` is derived from its recent statistics activity. On a normal user action
(check-in/receipt), `Laboratory.DeriveActiveFromActivity` → `ChangeStatus` raises a `LaboratoryStatusChanged`
domain event, which the `AuditAndOutboxInterceptor` turns into an outbox message on `SaveChanges`. The nightly
Lab-Stats sync also **re-derives every lab's status** from the full statistics history
(`OracleSyncRunner.DeriveLabStatusesAsync`). If that bulk pass emitted an event per changed lab, a single sync
could enqueue thousands of outbox messages — an amplification with no useful consumer, since the status is a
derived read-model value, not a business transition an operator initiated. The code therefore calls
`ChangeStatus` then `ClearDomainEvents()` before saving, suppressing the event. An adversarial-audit *opinion*
(STAT-013) asked that this deliberate suppression be recorded.

## Decision
During the sync's bulk status re-derivation, raise and then clear `LaboratoryStatusChanged` so the mutation is
persisted (and audited by the interceptor) but no outbox message is produced. User-initiated status changes are
unaffected — they still raise the event and drive downstream handlers/notifications.

## Alternatives considered
- **Let the sync emit events and rely on the outbox to absorb them.** Correct in the small, but a full
  re-derivation can flip many labs at once (e.g. after a large backfill), flooding the queue and the
  notification path with machine-driven "status changed" noise that no operator caused.
- **A separate silent setter (`SetDerivedStatus`) that never raises.** Cleaner intent than raise-then-clear,
  but duplicates the transition logic/validation in `ChangeStatus`; the raise-then-clear keeps a single
  transition path and is explicit about *what* is being suppressed and *why* (documented at the call site).
- **Batch/coalesce the events into one summary event.** Possible, but there is no consumer that needs
  per-sync status-change notifications today; adding one would be speculative.

## Consequences
- Downstream reactions to `LaboratoryStatusChanged` (notifications, realtime hints) fire for
  operator-initiated changes but **not** for sync-derived re-derivation. This is intended: the sync is a bulk
  reconciliation, and the new status is visible on the next read regardless.
- The status change is still persisted and audited; only the event/outbox side effect is suppressed.

## Risks
- If a future consumer genuinely needs to react to *every* status change (including sync-derived), this
  suppression would hide those. Mitigated by the call-site comment and this ADR.

## Revisit criteria
A requirement to notify on sync-derived status changes, or a move of status derivation out of the bulk sync
into an event-sourced/streamed model — either should replace the raise-then-clear with an explicit,
consumer-aware event (e.g. a coalesced summary event).
