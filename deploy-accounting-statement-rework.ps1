# Deploy: Accounting statement rework (feature/accounting-statement-rework) - penalties off the Deductions page and onto the
#         rep statement; Rep Statement debit/credit rebuilt; Penalty page area filter + LDM validation; Rep Income lists LDM-active labs.
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies the new EF migration on startup (MigrateAsync):
#   PenaltiesOffDeductions : DELETES every deduction row with origin = 'AutoPenalty' or reason = 'Penalty' (the mirrors were
#                            fully derived from penalty_record, which is untouched; manual Penalty-reason rows are removed
#                            with the reason), drops deduction.penalty_record_id (+ its FK / unique index), rewrites
#                            ck_deduction_origin (Manual / AutoDeal with Transportation / PercentageDeal only) and adds
#                            ix_detailed_registration_acc_no for the Penalty Report LDM validation.
#   Domain        : PenaltyRecord.PenaltyAmount = RIGHT - WRONG for every user type; DeductionReason loses Penalty,
#                   DeductionOrigin loses AutoPenalty (FromPenalty / RefreshFromPenalty gone)
#   Application   : penalty handlers no longer mirror deductions; SuggestDeduction = PercentageDeal only; GetPenalties gains
#                   areaId; PenaltyDto gains ResponsibleRepId/Name + LdmStatus/LdmNote; DeductionAutomationResult loses the
#                   penalty counters; LabLookupDto gains Area
#   Infrastructure: StatementAsync - Debit = Rep Income "total required" per day + the RIGHT test of every penalty; Credit = the
#                   actual collections (rep share, noted cash/bank/IBAN/done by/notes), the area deductions (Area + Responsible
#                   views) and the WRONG test of every penalty, each noted with its record. A Rep penalty follows the rep who
#                   made it; DataEntry / Technician / LabRequest follow the lab's Lab Responsible. Synced income and
#                   paid/delayed no longer post. RealIncomeSheetAsync also lists the rep's labs with LDM transactions that day
#                   (daily_lab_statistic) and sums every penalty type (right - wrong). PenaltiesAsync validates each Acc No
#                   against detailed_registration (exists / belongs to the lab / carries the entered tests). The deductions
#                   automation no longer links penalties.
#   wwwroot       : Deductions - no Penalty reason / KPI / mirrors; Penalty Statement - Area filter (page + record dialog),
#                   penalty preview = right - wrong; Penalty Report - all four user types, "LDM check" column with the reason,
#                   Lab Request rows show the Lab Responsible; Rep Statement - new line kinds + hints; Rep Income - penalty
#                   column = right - wrong of every type.
#
# TO KNOW AFTER THIS RELEASE:
#   - Existing AutoPenalty deductions and manual Penalty-reason deductions are REMOVED by the migration (the pg_dump taken
#     below is the only way back). Penalty records themselves are untouched and now appear on the rep statement.
#   - The rep statement debit is now what the Lab Responsibles entered as "total required" on Rep Income; days with no
#     sheet entry show no debit.
#   - The LDM check reads the synced registration lines (Detailed Statistics); a day that was never synced reports
#     "Acc No not in LDM" until it is.
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
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PenaltiesOffDeductions' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the PenaltiesOffDeductions migration - rebuild (pass -Build)." }
if (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'RefreshFromPenalty' -Quiet) { throw "ABORT: FollowUp.Domain.dll still carries the penalty mirror (RefreshFromPenalty) - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Application.dll" -Pattern 'LdmStatus' -Quiet)) { throw "ABORT: FollowUp.Application.dll lacks the LDM validation fields - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'PenaltyRight' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the reworked statement (PenaltyRight kind) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the reworked Accounting UI)."

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
} else { Write-Warning "Skipping database backup (-SkipDbBackup). The migration DELETES the penalty deductions with no dump to fall back on." }

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

Write-Host "Starting FollowUp service (applies the PenaltiesOffDeductions migration)..."
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
    Write-Host "  1. Accounting -> Deductions: only Transportation / Percentage Deal remain; the penalty rows are gone."
    Write-Host "  2. Accounting -> Penalty Statement: Area filter on the page and in Record penalty; Penalty = right - wrong."
    Write-Host "  3. Accounting -> Penalty Report: every user type, LDM check column (Acc No exists / belongs to the lab / carries the tests)."
    Write-Host "  4. Accounting -> Rep Statement: Debit = Rep Income total required + right tests; Credit = collections, deductions, wrong tests."
    Write-Host "  5. Accounting -> Rep Income: labs with LDM transactions that day appear even without a recorded visit; Penalty = right - wrong of every type."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
