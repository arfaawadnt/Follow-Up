# ADR-0012 — MediatR commands may serve as HTTP wire contracts for internal endpoints

**Status:** Accepted · 2026-08-31

## Context
Most write endpoints bind a MediatR command directly as the request body — e.g. `POST /sample-tracking` binds
`RecordSampleDataEntryCommand`, `POST /outsource-samples` binds `CreateOutsourceSampleCommand`. Verification
cycle 2 (finding **ST-7**) noted this couples the public wire contract to the internal command type; some
codebases interpose a dedicated request DTO for every endpoint. There is no architecture rule mandating either
style, so this is a deliberate convention, not a defect.

## Decision
Binding a command directly as the HTTP body is the **accepted convention** for this internal, single-tenant API
whenever the command's members are all client-supplied input. A **dedicated body record** is introduced only when
the wire shape must diverge from the command:

- the command carries **server-set** fields that must never be client-supplied (identity, timestamps, scope);
- the route is **resource-oriented** and part of the input comes from the path — e.g.
  `POST /esign/{module}/{recordId}/sign` (SIG-12);
- the endpoint needs a **typed response** shape distinct from the raw command result (CMP-13, CPN-15).

Computed members such as `RequiredPrivileges` are get-only and ignored by the model binder, so exposing a command
as the body type creates no security or contract hazard.

## Consequences
- SampleTracking and similar endpoints keep binding their commands directly — no per-endpoint DTO ceremony.
- The three diverging cases already use dedicated body records: `LogComplaintBody` (CMP-13), `SignActionBody`
  (SIG-12), `CompensationConfigBody` (CPN-15).

## Revisit criteria
- The API becomes a **published/versioned public or partner contract** → introduce a request/response DTO layer
  fully decoupled from the internal commands, and supersede this ADR.
