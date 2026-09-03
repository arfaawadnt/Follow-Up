# Build Assumptions & Open Questions

Points where the spec is silent or ambiguous and a decision was made to keep momentum.
Each is low-risk to revise (isolated behind a value object / config). Flag for confirmation.

| # | Area | Spec says | Assumption made | Revisit cost |
|---|------|-----------|-----------------|--------------|
| A1 | `LaboratoryStatus` members | "lab status CHECK (8 values)" — the 8 are **not enumerated** anywhere in the four docs. | Modeled as: **New, Scanned, Active, Inactive, Pending, Suspended, Stopped, Churned** (derived from the design-system badge domain-state mapping + BR-5 activity-derived lifecycle). | Low — single value object + CHECK constraint. |
| A2 | Lab-status auto-derivation rule (BR-5) | "auto-derived from activity (e.g. on check-in/receipt)" — exact rule unspecified. | On lab check-in/receipt an Inactive/Pending lab is promoted to Active; other derivations TBD. | Low — one domain method. |
| A3 | Loyalty tier thresholds & commission formula | "compensation config defines both formulas" — actual numbers not given. | Formulas are data-driven from a `compensation_config` document; seed with placeholder tiers/rates. | Low — config-driven. |
| A4 | Complaint stage names (staged investigation) | logged → acknowledge → validity → investigation/root-cause → business outcome → resolution. | Stage enum uses those six names; stage is metadata, status machine is authoritative (fixes CMP-STAGE). | Low. |
| A5 | Exact 116-route contract | Doc enumerates ~90 routes by name; full set is code-generated OpenAPI. | Implement every named route; derive the remainder from FR acceptance criteria; treat OpenAPI as source of truth once built. | Medium — additive. |

_Update this table whenever a guess is confirmed or corrected._
