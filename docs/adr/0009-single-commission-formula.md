# ADR-0009 — Commission uses one sample-count formula for all rep types

**Status:** Accepted · 2026-08-29

## Context
The commission engine (`CompensationCalculator.ComputeCommission`) computes the same figure for every
representative: `commission = CommissionRatePercent% × achieved-sample-count`, plus a flat bonus once
achievement reaches `BonusThresholdPercent` (SRS FR-12, BR-9). `Representative` also carries `GoalType`,
`GoalDuration` (Monthly/Quarterly) and `Metric`.

Verification cycle 2 (finding **CPN-10**) flagged that these three fields are not read by the commission engine,
which *implies* that goal-variant commission formulas exist (e.g. income-based instead of sample-count, or a
quarterly attainment basis) — and warned against inventing such a rule ("Do not invent critical business
rules", 158). The finding was **INFERRED**: it could not tell whether variant formulas were intended-and-missing
or whether the single formula is correct and the fields serve another purpose.

Investigation (2026-08-29) established the latter. The fields are **not vestigial** — they drive reporting, not
commission:
- `GoalDuration` selects the monthly-vs-quarterly target basis in the rep-performance/pacing report
  (`NotificationInsightsQueries`, `r.GoalDuration == GoalDuration.Monthly`).
- `GoalType` and `Metric` are display labels surfaced in the representative and commission list DTOs.

## Decision
Confirmed with the product owner (2026-08-29): the commission formula is **deliberately a single
sample-count-based formula for all representative types**. `GoalType`/`GoalDuration`/`Metric` are
representative-profile and **reporting** attributes; they are intentionally **not** inputs to
`CompensationCalculator`. They are retained (reporting depends on them), not dropped.

The alternative — "drop the unused fields" — was rejected because the fields are live in the reporting read
paths; deleting them would break the rep-performance report and the list DTOs for no benefit.

## Consequences
- No goal-variant commission formulas: rate × sample count for everyone (BR-9). A code comment in
  `ComputeCommission` records this so the absence of a branch on `GoalType/Metric/GoalDuration` reads as
  intentional, not an omission.
- `GoalDuration` remains a real behavioural input to the pacing report; `GoalType`/`Metric` remain display data.

## Revisit criteria
- If a money-based commission (e.g. via `DailyLabStatistic.Income`) or a period-varying attainment basis is ever
  required for some rep type, that is a **new** product decision: branch `ComputeCommission` on the relevant
  field, decide retroactivity for historical payout recomputation, and supersede this ADR.
