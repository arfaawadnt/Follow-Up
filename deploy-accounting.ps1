# Deploy: Accounting module + Lab "Credit" / Area "Percentage Deal" (PR #9, branch feature/accounting-module).
# -------------------------------------------------------------------------------------------------
# FULL release — ships the 4 managed DLLs + the Angular bundle AND applies 2 new EF migrations on startup (MigrateAsync):
#   AreaPercentageDealAndLabCredit : laboratory.credit; area.percentage_deal + area.percentage (numeric(5,2)) + 2 CHECKs
#   AddAccountingModule            : 7 tables (treasury_reason, treasury, treasury_entry, penalty_record, deduction,
#                                    collection, rep_income_entry), 5 identity serials, 6 Restrict FKs, 9 CHECKs
#   Domain        : Accounting aggregates + enumerations; Laboratory.Credit; Area.PercentageDeal/Percentage;
#                   ViewAccounting / ManageAccounting privileges
#   Application   : Features/Accounting slice (queries, commands, validators, scope guards); area/lab field threading
#   Infrastructure: EF configs, repositories, AccountingQueries (rep statement + deduction suggestions), migrations
#   Api           : /api/v1/accounting endpoints; AreaBody + lab commands carry the new fields
#   wwwroot       : "Accounting" nav group after Statistics with 5 report pages; Lab Credit checkbox; Setup › Areas
#                   Percentage Deal checkbox + field; EN/AR strings
#
# Startup side effects (idempotent): the seeder backfills the built-in Admin role with the two new privileges.
# Other roles must be GRANTED ViewAccounting / ManageAccounting in Setup before the Accounting group appears for them.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to
# rebuild. Takes a pg_dump (custom format) of the live DB before touching anything; -SkipDbBackup to skip.
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

# Prod is built from main (see the 2026-09-04 regression). The deployed main is 81052a4; this release is that commit
# plus the three accounting commits, so building from the feature branch loses nothing — but flag any drift.
$head   = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$dirty  = (& git -C $repo status --porcelain -- src web) | Where-Object { $_ }
if ($head -ne 'main') { Write-Warning "Checked-out branch is '$head', not 'main'. Merge PR #9 into main first, or confirm this branch is deployed-main + the accounting commits only." }
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

foreach ($d in $dlls) {
    if (-not (Test-Path "$srcBin\$d")) { throw "Missing $srcBin\$d - build first (pass -Build)." }
}
if (-not (Test-Path "$srcWeb\index.html")) { throw "Missing $srcWeb\index.html - build the Angular bundle first (pass -Build)." }
# The payload must actually carry this release (guards against deploying a stale build).
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'AddAccountingModule' -Quiet)) {
    throw "ABORT: FollowUp.Infrastructure.dll does not contain the AddAccountingModule migration - rebuild (pass -Build)."
}
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'PenaltyRecord' -Quiet)) {
    throw "ABORT: FollowUp.Domain.dll does not contain the Accounting aggregates - rebuild (pass -Build)."
}
$accChunks = @(Get-ChildItem "$srcWeb\chunk-*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'app-acc-(penalties|deductions|treasury|collections|rep-statement)' })
if ($accChunks.Count -lt 5) { throw "ABORT: the Angular bundle carries $($accChunks.Count) of 5 Accounting pages - rebuild the bundle (pass -Build)." }

# ---- Database backup (pg_dump custom format) using the service's own FOLLOWUP_DB connection string ----------
if (-not $SkipDbBackup) {
    $svcEnv = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\FollowUp' -Name Environment).Environment
    $cs = ($svcEnv | Where-Object { $_ -like 'FOLLOWUP_DB=*' } | Select-Object -First 1)
    if (-not $cs) { throw "FOLLOWUP_DB not found in the FollowUp service environment - cannot back up (pass -SkipDbBackup to override)." }
    $cs = $cs.Substring('FOLLOWUP_DB='.Length)
    $kv = @{}
    foreach ($part in ($cs -split ';')) {
        if ($part -match '^\s*([^=]+?)\s*=\s*(.*?)\s*$') { $kv[$matches[1].ToLowerInvariant()] = $matches[2] }
    }
    $pgHost = if ($kv['host']) { $kv['host'] } elseif ($kv['server']) { $kv['server'] } else { 'localhost' }
    $pgPort = if ($kv['port']) { $kv['port'] } else { '5432' }
    $pgDb   = $kv['database']
    $pgUser = if ($kv['username']) { $kv['username'] } elseif ($kv['user id']) { $kv['user id'] } else { $kv['user'] }
    if (-not $pgDb -or -not $pgUser) { throw "Could not parse Database/Username from FOLLOWUP_DB - back up manually or pass -SkipDbBackup." }
    $dumpFile = "C:\FollowUp\db-backup-accounting-$stamp.dump"
    Write-Host "Backing up database '$pgDb' on ${pgHost}:$pgPort -> $dumpFile"
    $env:PGPASSWORD = $kv['password']
    try {
        & $pgDump -h $pgHost -p $pgPort -U $pgUser -d $pgDb -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE). Aborting before any change." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $size = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
    Write-Host "Database backup OK ($size MB). Restore with: pg_restore -h $pgHost -p $pgPort -U $pgUser -d $pgDb --clean --if-exists $dumpFile"
} else { Write-Warning "Skipping database backup (-SkipDbBackup). 2 migrations will apply on startup with no dump to fall back on." }

# ---- App backup ----------------------------------------------------------------------------------------------
$backup = "C:\FollowUp\app-backup-accounting-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Stopping FollowUp service..."
if ((Get-Service FollowUp).Status -ne 'Stopped') {
    Stop-Service FollowUp -Force
    (Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

# 'Stopped' can precede the host process releasing file handles — wait until a DLL is exclusively openable.
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

Write-Host "Starting FollowUp service (applies 2 migrations + Admin privilege backfill)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# Migrations run before Kestrel listens; poll the ready endpoint (checks the DB) rather than a fixed sleep.
$healthy = $false
for ($i = 0; $i -lt 45; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri 'http://localhost:5088/healthz/ready' -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    if ((Get-Service FollowUp).Status -ne 'Running') { break }
}
if ($healthy) {
    Write-Host "Service is up and healthy. App backup at $backup"
    Write-Host "Verify (hard-refresh the browser, Ctrl+F5):"
    Write-Host "  Sidebar -> new 'Accounting' group after Statistics with 5 pages (Admin sees it immediately)."
    Write-Host "  Treasury Account -> 'Treasury Setup' tab: add a reason + a treasury (branches), then record a Debit entry."
    Write-Host "  Penalty Statement -> record a penalty (lab, wrong/right tests) -> penalty = wrong - right."
    Write-Host "  Deductions -> Penalty / Percentage Deal reason -> 'Suggest value' prefills from the period."
    Write-Host "  Collection -> Bank amount reveals the IBAN pick (12/16/18); Group type takes several reps."
    Write-Host "  Rep Statement -> pick a rep: synced + real income (Debit) vs collections (Credit), running balance."
    Write-Host "  Setup -> Areas: 'Percentage Deal' checkbox reveals the % field; Labs -> 'Credit' checkbox."
    Write-Host "  Grant ViewAccounting / ManageAccounting to non-Admin roles that need the module."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if migrations partially applied, start the service."
}
