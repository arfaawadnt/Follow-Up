# Email Reports & SMTP Mail Gateway

Scheduled daily-statistics emails and the in-app SMTP configuration that sends them. Recipients receive
a branded HTML summary plus **one styled `.xlsx` attachment per report** (Lab / Test / Area) carrying the
full matched data. The **Area Statistics** attachment is grouped by governorate → area and colour-coded
against a reference month, matching the on-screen Area Statistics page and its export.

Verified against the codebase on 2026-09-04 (feature live in production, `origin/main`).

## Capabilities

- **Mail Gateway (SMTP)** — operator-editable outgoing mail server, no redeploy needed. Singleton config
  with a masked, write-only password. A **Send test** button validates auth end to end.
- **Scheduled reports** — any number of subscriptions, each choosing which reports to include, optional
  filters, recipients (app users + free-form emails), a send time (Cairo), and a rolling window.
- **Excel attachments** — every included report is attached as a dependency-free `.xlsx` matching the
  on-screen exports (banded frozen header, thin borders, auto-filter, thousands-separated numbers).
- **Area colour coding** — the Area sheet reproduces the page: governorate bands (bold, light fill) each
  followed by their areas, with per-day columns flagged **green** when a day beats the reference month's
  daily average and **red** when it falls short.

## Access

Guarded by the `ManageEmailReports` privilege (`FollowUp.Domain.Identity.Privileges`). Admin gets it via
the seeder backfill to `Privileges.All`; grant it to other roles on the Roles page. UI route:
`/email-reports` (nav item `email_reports`).

## Data model

Two additive tables (migration `20260904192258_AddEmailReports`, no Oracle feed):

| Table | Aggregate | Notes |
|---|---|---|
| `smtp_config` | `SmtpConfig` (singleton, id `"smtp"`) | `Enabled, Host, Port, UseSsl, FromAddress, User, Password`. Password stored as text, **never returned** by queries; `SetPassword` overwrites only when a new value is supplied. |
| `stats_email_subscription` | `StatsEmailSubscription` | Reports included (Lab/Test/Area), `FiltersJson`, `UserIds`/`Emails` (jsonb), `SendHour`/`SendMinute`, `WindowDays`, `Enabled`, `LastRunAt`, `LastStatus`. |

Domain types: `src/FollowUp.Domain/Emailing/EmailReports.cs`.

### Filters JSON

`FiltersJson` is a free blob written by the editor and deserialized by the runner into:

```
Governorates[], Cities[], Areas[], Categories[], Segments[], Groups[], RefMonth ("YYYY-MM")
```

An empty array means "all". `RefMonth` drives the Area colour coding; empty falls back to the calendar
month before the report window.

## Architecture

Backend follows the standard layering (Domain → Application → Infrastructure/Api):

| Concern | Location |
|---|---|
| CQRS (SMTP config, subscriptions, send-now, test) + validators | `src/FollowUp.Application/Features/EmailReports/EmailReports.cs` |
| Repository / query / scheduler / runner interfaces | `src/FollowUp.Application/Common/Abstractions/{IEmailReportsServices,Persistence/IEmailReportsRepositories}.cs` |
| `IEmailSender` (+ attachments overload) and `EmailAttachment` | `src/FollowUp.Application/Common/Abstractions/Gateways.cs` |
| Repositories, queries, Hangfire scheduler, **runner** | `src/FollowUp.Infrastructure/Emailing/EmailReportsInfrastructure.cs` |
| **`.xlsx` writer** | `src/FollowUp.Infrastructure/Emailing/XlsxWriter.cs` |
| SMTP sender (attaches files) | `src/FollowUp.Infrastructure/Gateways/EmailWhatsAppGateways.cs` (`SmtpEmailSender`) |
| EF configuration | `src/FollowUp.Infrastructure/Persistence/Configurations/EmailReportsConfigurations.cs` |
| HTTP endpoints (`/email/smtp`, `/email/subscriptions`, `.../send-now`, `/email/smtp/test`) | `src/FollowUp.Api/Endpoints/EmailReportsEndpoints.cs` |
| Angular page | `web/src/app/features/emailreports/emailreports.component.ts` |

### Scheduling

Each enabled subscription registers a Hangfire recurring job `stats-email-{id}` with cron
`{minute} {hour} * * *` in the **Africa/Cairo** timezone (`StatsEmailScheduler`). Disabling a subscription
removes its job. `SyncAllAsync` re-registers all jobs on startup (`RecurringJobsInitializer`).

### The runner (`StatsEmailRunner`)

On fire (or **Send now**) it computes a window ending yesterday (`WindowDays` back), deserializes the
filters, and builds one `ReportSection` per included report. Each section carries **both** a capped HTML
table (email body) and the full data for the `.xlsx` attachment. Reports are queried with
`OrgScope.Global` and filtered in memory. Recipients = active users' emails + the free-form list;
`RecordRun` stores the outcome (`sent=… failed=…`, with the last error when a send fails).

## The `.xlsx` writer

`XlsxWriter` is dependency-free (SpreadsheetML packed with `System.IO.Compression.ZipArchive`) and is a
**1:1 port of the browser export util** (`web/src/app/shared/export.util.ts`) — identical fonts, fills and
cell-style indices — so an emailed sheet looks the same as "Export Excel" on the page. It's used instead of
a library because the app CSP is `script-src 'self'`.

- `Build(sheetName, headers, IReadOnlyList<object?[]>)` — plain rows (Lab / Test).
- `Build(sheetName, headers, IReadOnlyList<XlsxCell[]>)` — styled rows. `XlsxCell` carries a value plus a
  colour `Fill` (`Pos` green / `Neg` red / `Gov` band) and `Bold`; strings and numeric types convert
  implicitly. Numbers are written as real numeric cells so Excel can sum and sort.

### Area sheet layout

Columns: `Governorate · Area · Real Name · Ref by Month · Ref by Day · Total Test Count · Total Income`
followed by one column per day in the window. A governorate band row (Gov fill, bold) precedes its area
rows (Area column filled, Governorate column blank). Day cells use the daily baseline
`refMonthTotal / daysInReferenceMonth`: `> baseline` → green, `< baseline` → red. This mirrors
`AreaStatsComponent.exportRows()` in `web/src/app/features/areastats/areastats.component.ts`.

## SMTP configuration (Gmail)

Set on **Email Reports → Mail Gateway (SMTP)**:

| Field | Gmail value |
|---|---|
| Host / Port / SSL | `smtp.gmail.com` / `587` / on (STARTTLS) |
| From address | the sending mailbox |
| **Username** | the **full email address** (e.g. `name@gmail.com`) — **not** a short name |
| **Password** | a 16-char **App Password** (2-Step Verification on; myaccount.google.com/apppasswords) |

> A short username or the normal account password both fail with `535 / 5.7.0 Authentication Required`.
> Gmail authenticates with the full address and an App Password only.

Leave the password field blank on a later save to keep the stored one. The DB `smtp_config` is
authoritative; when no row exists the sender falls back to the legacy `Smtp` config section so existing
env-var deployments keep working.

## Deployment

`deploy-email-xlsx.ps1` (elevated PowerShell) — ships the managed DLLs + the Angular bundle, no migration:
backs up the current DLLs + `wwwroot`, stops the `FollowUp` service, copies, restarts, health-checks, and
prints the rollback path. Pass `-Build` to build first (also applies the CSP `index.html` fix). See
[deploy & Oracle integration notes](../CLAUDE.md) style scripts alongside `deploy-areastats.ps1`.

## Operational notes / gotchas

- **Send test from the UI, not a script.** Triggering a send from an ad-hoc PowerShell/Bash script (reading
  the stored SMTP password from the DB) is intentionally blocked; use the app's **Send test** button, which
  keeps the credential inside the app.
- **Attachments overload.** Only `SmtpEmailSender` implements `IEmailSender`; both the plain and the
  attachments overloads live there (the plain one delegates to the attachments one with an empty list).
- **HTML preview is capped** at 100 rows per report; the attachment always has the full data.
- **New commands need validators** — the architecture ratchet fails otherwise (see the delete/send-now
  validators in the Application feature file).

## Extension ideas

- Group / colour-code the Lab and Test sheets (today they are flat tables).
- Colour-code the Area **email body** table too (currently a compact preview; the attachment is the rich one).
- Per-subscription "view" (daily / monthly / yearly) for the Area columns instead of the fixed daily view.
