#!/usr/bin/env bash
# One-shot: reset the FollowUp dev Postgres password for user "postgres".
# Opens a temporary loopback-only trust window, sets the password, then restores scram-sha-256.
set -e
HBA="/d/FollowUpDevDb/pgdata/pg_hba.conf"
BIN="D:/followup/pgsql/pgsql/bin"
DATA="D:/FollowUpDevDb/pgdata"
NEWPW='FollowUp_Dev_2026!'      # <-- change this if you want a different password

echo "1/5 backing up pg_hba.conf..."
cp "$HBA" "$HBA.bak.followup"

echo "2/5 opening temporary loopback trust window..."
printf 'host    all    postgres    127.0.0.1/32    trust\n%s' "$(cat "$HBA")" > "$HBA.tmp" && mv "$HBA.tmp" "$HBA"
"$BIN/pg_ctl.exe" -D "$DATA" reload

echo "3/5 setting new password..."
"$BIN/psql.exe" -h 127.0.0.1 -p 5442 -U postgres -d followup -c "ALTER USER postgres PASSWORD '$NEWPW';"

echo "4/5 restoring original scram-sha-256 auth..."
cp "$HBA.bak.followup" "$HBA"
"$BIN/pg_ctl.exe" -D "$DATA" reload

echo "5/5 verifying login with the new password..."
PGPASSWORD="$NEWPW" "$BIN/psql.exe" -h 127.0.0.1 -p 5442 -U postgres -d followup -c "\dt" >/dev/null && echo "OK - password is now: $NEWPW"
