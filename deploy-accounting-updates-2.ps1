# Deploy: Accounting updates round 2 (feat/accounting-updates-2) - Data Entry penalties name the reviewer too and the pickers
#         follow the lab / the Sample Lifecycle Tracking; Penalty Report "Performed by" filter + Total Penalties PDF;
#         Collection governorate filter, bank Reference Number and Out-source income; Rep Statement "View as" grouping.
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies the new EF migration on startup (MigrateAsync):
#   PenaltyReviewerCollectionOutsource : ADDS penalty_record.reviewed_by_user_id (FK app_user, index) with
#                            ck_penalty_record_reviewed_by (only a DataEntry row names a reviewer); ADDS collection.outsource_income
#                            (numeric(18,2), default 0) and collection.reference_number (varchar 100, index) with
#                            ck_collection_outsource_within_total and ck_collection_reference_iff_bank. Additive only, no data
#                            is rewritten; plain CHECKs on small tables (well inside the 30 s service-start window).
#   Domain        : PenaltyRecord.ReviewedByUserId (required for DataEntry, refused otherwise); Collection.OutsourceIncome
#                   (0 <= x <= cash + bank), ReferenceNumber (only with a bank amount, cleared with it), NetIncome, NetShareOf
#   Application   : Create/UpdatePenalty + Create/UpdateCollection carry the new fields; GetPenaltyActors takes laboratoryId /
#                   date / step (ViewAccounting now, so the report filter can use it); PenaltyDto gains ReviewedById/Name;
#                   CollectionDto gains OutsourceIncome / ReferenceNumber / NetIncome
#   Infrastructure: PenaltyActorsAsync - Rep: the reps linked to the lab (responsible / collectors / marketing), every rep when
#                   none; DataEntry with lab + date + step: the users who did that step on the area's Sample Lifecycle
#                   Tracking row that day, every user when nobody; LabRequest: the Lab Responsibles. StatementAsync credits
#                   the rep's NET share (cash + bank - out-source, spread over a group by share) and notes ref / out-source.
#   wwwroot       : Penalty Statement - Data Entry By + Reviewed By pickers (DataEntry), Rep picker per lab, reviewer shown;
#                   Penalty Report - Performed by filter (per user type), reviewer shown, "Total Penalties (PDF)" per person
#                   grouped by user type (a data-entry penalty counts for both users); Collection - Governorate filter (page +
#                   dialog), Reference Number (bank only) + grid filter + "bank records without reference", Out-source income
#                   + Collection income column; Rep Statement - View as Daily / Weekly / Monthly / Yearly.
#
# TO KNOW AFTER THIS RELEASE:
#   - Existing DataEntry penalties have no reviewer; editing one asks for the reviewer before it can be saved.
#   - Existing collections carry out-source 0 and no reference; a bank collection without a reference shows "Missing" and
#     is what the "Bank records without Reference Number" filter lists.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to build.
# Takes a pg_dump (custom format) of the live DB before touching anything; -SkipDbBackup to skip.
# -------------------------------------------------------------------------------------------------
param([switch]$Build, [switch]$SkipDbBackup)

$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$repo   = 'D:\App'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$srcWeb = "$repo\src\FollowUp.Api\wwwroot"
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')
$dotnet = 'C:\dotnet\dotnet.exe'
$nodeDir = 'C:\nodejs'
$pgDump = 'C:\Program Files\PostgreSQL\17\bin\pg_dump.exe'
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'

$head  = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$dirty = (& git -C $repo status --porcelain -- src web) | Where-Object { $_ }
if ($head -ne 'main') { Write-Warning "Checked-out branch is '$head', not 'main'. Merge the PR into main first (prod is built from main)." }
if ($dirty) { Write-Warning "Uncommitted changes under src/ or web/:`n$($dirty -join "`n")" }

if ($Build) {
    Write-Host "Building Release DLLs..."
    & $dotnet build "$repo\FollowUp.sln" -c Release --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
    Write-Host "Building Angular bundle..."
    $env:Path = "$nodeDir;" + $env:Path
    Push-Location "$repo\web"
    try { & "$nodeDir\npm.cmd" run build; if ($LASTEXITCODE -ne 0) { throw "ng build failed (exit $LASTEXITCODE)." } }
    finally { Pop-Location }
}

# CSP fix: strip media="print" onload="this.media='all'" (the app CSP blocks the inline onload). Idempotent.
$index = "$srcWeb\index.html"
if (Test-Path $index) {
    $html = Get-Content $index -Raw
    $fixed = $html -replace '\s*media="print"', '' -replace '\s*onload="this\.media=''all''"', ''
    if ($fixed -ne $html) {
        [System.IO.File]::WriteAllText($index, $fixed, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Applied CSP stylesheet fix to index.html."
    } else { Write-Host "CSP fix already applied (or not needed)." }
    if ($fixed -match 'media="print"') { throw "ABORT: CSP fix failed (print-onload still present in index.html)." }
}

foreach ($d in $dlls) { if (-not (Test-Path "$srcBin\$d")) { throw "Missing $srcBin\$d - build first (pass -Build)." } }
if (-not (Test-Path "$srcWeb\index.html")) { throw "Missing $srcWeb\index.html - build the Angular bundle first (pass -Build)." }
# Payload guards (refuse a stale build). Type / member names are UTF-8 metadata and searchable; string literals are not.
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PenaltyReviewerCollectionOutsource' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the PenaltyReviewerCollectionOutsource migration - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'ReviewedByUserId' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks the penalty reviewer (ReviewedByUserId) - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Application.dll" -Pattern 'OutsourceIncome' -Quiet)) { throw "ABORT: FollowUp.Application.dll lacks the collection out-source fields - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'view_weekly' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the Rep Statement View-as grouping (view_weekly) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the round-2 Accounting UI)."

# ---- Database backup (pg_dump custom format) using the service's own FOLLOWUP_DB connection string ----------
if (-not $SkipDbBackup) {
    $svcEnv = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\FollowUp' -Name Environment).Environment
    $cs = ($svcEnv | Where-Object { $_ -like 'FOLLOWUP_DB=*' } | Select-Object -First 1)
    if (-not $cs) { throw "FOLLOWUP_DB not found in the FollowUp service environment - cannot back up (pass -SkipDbBackup to override)." }
    $cs = $cs.Substring('FOLLOWUP_DB='.Length)
    $kv = @{}
    foreach ($part in ($cs -split ';')) { if ($part -match '^\s*([^=]+?)\s*=\s*(.*?)\s*$') { $kv[$matches[1].ToLowerInvariant()] = $matches[2] } }
    $pgHost = if ($kv['host']) { $kv['host'] } elseif ($kv['server']) { $kv['server'] } else { 'localhost' }
    $pgPort = if ($kv['port']) { $kv['port'] } else { '5432' }
    $pgDb   = $kv['database']
    $pgUser = if ($kv['username']) { $kv['username'] } elseif ($kv['user id']) { $kv['user id'] } else { $kv['user'] }
    if (-not $pgDb -or -not $pgUser) { throw "Could not parse Database/Username from FOLLOWUP_DB - back up manually or pass -SkipDbBackup." }
    $dumpFile = "C:\FollowUp\db-backup-statement-rework-$stamp.dump"
    Write-Host "Backing up database '$pgDb' on ${pgHost}:$pgPort -> $dumpFile (takes ~3 minutes; the service keeps running meanwhile)"
    $env:PGPASSWORD = $kv['password']
    try {
        & $pgDump -h $pgHost -p $pgPort -U $pgUser -d $pgDb -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE). Aborting before any change." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $size = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
    Write-Host "Database backup OK ($size MB). Restore with: pg_restore -h $pgHost -p $pgPort -U $pgUser -d $pgDb --clean --if-exists $dumpFile"
} else { Write-Warning "Skipping database backup (-SkipDbBackup). The migration is additive (new columns + CHECKs) but there is no dump to fall back on." }

# ---- App backup ----------------------------------------------------------------------------------------------
$backup = "C:\FollowUp\app-backup-statement-rework-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Stopping FollowUp service..."
if ((Get-Service FollowUp).Status -ne 'Stopped') {
    Stop-Service FollowUp -Force
    (Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}
$lockProbe = "$app\FollowUp.Application.dll"
$unlocked = $false
for ($i = 0; $i -lt 40; $i++) {
    try { $fs = [System.IO.File]::Open($lockProbe, 'Open', 'ReadWrite', 'None'); $fs.Close(); $unlocked = $true; break }
    catch { Start-Sleep -Milliseconds 1000 }
}
if (-not $unlocked) { throw "FollowUp.Application.dll still locked after 40s - a host process is holding it; stop it and re-run." }

Write-Host "Copying DLLs..."
foreach ($d in $dlls) { Copy-Item "$srcBin\$d" $app -Force }
Write-Host "Mirroring wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service (applies the PenaltyReviewerCollectionOutsource migration)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

$healthy = $false
for ($i = 0; $i -lt 45; $i++) {
    Start-Sleep -Seconds 2
    try { $r = Invoke-WebRequest -Uri 'http://localhost:5088/healthz/ready' -UseBasicParsing -TimeoutSec 5; if ($r.StatusCode -eq 200) { $healthy = $true; break } } catch { }
    if ((Get-Service FollowUp).Status -ne 'Running') { break }
}
if ($healthy) {
    Write-Host "Service is up and healthy. Backup at $backup"
    Write-Host "Next (Ctrl+F5):"
    Write-Host "  1. Accounting -> Penalty Statement: Record penalty, User Type = Data Entry -> Data Entry By + Reviewed By (users tracked for the lab area that day); User Type = Rep -> reps linked to the lab."
    Write-Host "  2. Accounting -> Penalty Report: pick a User Type, then Performed by; 'Total Penalties (PDF)' prints the per-person totals grouped by user type."
    Write-Host "  3. Accounting -> Collection: Governorate filter (page + dialog), Reference Number on bank collections + 'Bank records without Reference Number', Out-source income + Collection income."
    Write-Host "  4. Accounting -> Rep Statement: View as Daily / Weekly / Monthly / Yearly; a collection credits cash + bank - out-source."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
