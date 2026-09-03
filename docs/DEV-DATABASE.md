# Local Dev Database

A dedicated, isolated PostgreSQL 17 cluster was provisioned for development — separate from any other
instance on the host. It is **not** committed to the repo, and its password is **not** stored in source
(supplied out-of-band; provide it via the `FOLLOWUP_DB` environment variable).

| Setting | Value |
|---------|-------|
| Host / Port | `127.0.0.1` : `5442` |
| Database | `followup` |
| Username | `postgres` |
| Password | provided separately — set in `FOLLOWUP_DB`, never in source/git |
| Data directory | `D:\FollowUpDevDb\pgdata` (outside the repo) |
| Server log | `D:\FollowUpDevDb\server.log` |
| Binaries | `D:\followup\pgsql\pgsql\bin` (initdb / pg_ctl / psql) |

## Connection string
Set the environment variable (the design-time factory and the API read it):
```
FOLLOWUP_DB=Host=127.0.0.1;Port=5442;Database=followup;Username=postgres;Password=<the-password>
```

## Start / stop the cluster
```bash
# start
"D:/followup/pgsql/pgsql/bin/pg_ctl.exe" -D "D:/FollowUpDevDb/pgdata" -o "-p 5442" -l "D:/FollowUpDevDb/server.log" start
# stop
"D:/followup/pgsql/pgsql/bin/pg_ctl.exe" -D "D:/FollowUpDevDb/pgdata" stop
```
> Not registered as a Windows service — it does not auto-start on reboot. Register one later if desired.

## Apply migrations
```bash
FOLLOWUP_DB="Host=127.0.0.1;Port=5442;Database=followup;Username=postgres;Password=<pw>" \
  dotnet ef database update --project src/FollowUp.Infrastructure --startup-project src/FollowUp.Infrastructure
```

## Verified (2026-08-15)
- Both migrations applied: 31 domain tables (+ `__EFMigrationsHistory`), 21 FKs, 8 enum CHECK constraints.
- Append-only audit trail proven: INSERT ok; UPDATE/DELETE refused by trigger; DELETE allowed only with
  `SET followup.allow_audit_purge='on'` (the bounded retention purge path).
- CHECK constraints proven: an invalid `complaint.status` was rejected.
