# Deploy: Auditing -> Registration Changes (feat/registration-changes)
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed FollowUp DLLs + the Angular bundle AND applies one EF migration on startup (MigrateAsync):
#   Migration     : RegistrationChanges - new table registration_change (LDM REG_LOG edits joined to the registration; unique
#                   trans_id, indexes on modified_date / reg_date / lab_code) + stats_email_subscription.include_reg_changes. Instant.
#   Domain        : RegistrationChange aggregate; StatsEmailSubscription.IncludeRegChanges; privilege ViewRegistrationChanges
#                   (implied by ManageUsers; Admin gets it from the seeder on startup).
#   Infrastructure: new allow-listed Oracle feed "RegLog" (provisioned at startup from the default SQL; no env override set),
#                   OracleSyncRunner.RunRegistrationChangesAsync (window on the modification date), nightly job regchanges-sync
#                   at 00:20 Cairo (yesterday), RegistrationChangeQueries, email section "Registration Changes" (xlsx + pdf).
#   Api           : GET /registration-changes?regFrom&regTo&modFrom&modTo, POST /registration-changes/sync
#   wwwroot       : new nav group "Auditing" with the Registration Changes page (8 statistics cards, reg-date / modification-date
#                   ranges, change type, user, geography, lab, delay, search; grid with old -> new values, summary row, pager,
#                   Sync from Oracle dialog, Excel / PDF, help EN/AR); Email Reports gains the "Registration Changes" section;
#                   Roles page gains the "Registration Changes" row. Users need a RE-LOGIN to see the new group.
#   NOTE: the table starts empty - run "Sync from Oracle" on the page for the history you want (3 months per run).
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to build.
# Takes a pg_dump (custom format) of the live DB before touching anything; -SkipDbBackup to skip.
# -------------------------------------------------------------------------------------------------
param([switch]$Build, [switch]$SkipDbBackup)

$ErrorActionPreference = 'Stop'
$repo   = 'D:\App'
$app    = 'C:\FollowUp\app'
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
# Payload guards (refuse a stale build). Member names are UTF-8 metadata and searchable.
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'RegistrationChanges' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the RegistrationChanges migration - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'RegistrationChanges' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks RegistrationChanges - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PdfWriter' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll predates the 2026-09-20 release - rebuild from main (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'registration-changes' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the Rep Income Revision UI (registration-changes) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the Rep Income Revision page)."

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
    $dumpFile = "C:\FollowUp\db-backup-registration-changes-$stamp.dump"
    Write-Host "Backing up database '$pgDb' on ${pgHost}:$pgPort -> $dumpFile (takes ~3 minutes; the service keeps running meanwhile)"
    $env:PGPASSWORD = $kv['password']
    try {
        & $pgDump -h $pgHost -p $pgPort -U $pgUser -d $pgDb -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE). Aborting before any change." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $size = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
    Write-Host "Database backup OK ($size MB). Restore with: pg_restore -h $pgHost -p $pgPort -U $pgUser -d $pgDb --clean --if-exists $dumpFile"
} else { Write-Warning "Skipping database backup (-SkipDbBackup). The migration will apply on startup with no dump to fall back on." }

# ---- Backup of the replaced files ----
$backup = "C:\FollowUp\app-backup-registration-changes-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null
Write-Host "Backed up current DLLs + wwwroot -> $backup"

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

Write-Host "Starting FollowUp service (applies the RegistrationChanges migration)..."
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
    Write-Host "Next (Ctrl+F5): Statistics -> Detailed Statistics -> Sync from Oracle for the days you want the creation times on,"
    Write-Host "  then Apply Filters: Reg Created / Test Created columns, blue / red rows, 'Test Addition Status' filter."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
