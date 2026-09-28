# Deploy: Detailed Statistics - registration / test creation times + "Test Addition Status" (feat/detailed-test-addition)
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed FollowUp DLLs + the Angular bundle AND applies one EF migration on startup (MigrateAsync):
#   Migration     : DetailedRegistrationCreatedDates - two nullable columns on detailed_registration
#                   (reg_created_at, test_created_at; timestamp without time zone). No default, no index: instant.
#   Domain        : DetailedRegistration.RegCreatedAt / TestCreatedAt + TestAddition (None | Within3Hours | Over3Hours).
#   Infrastructure: DetailedStats Oracle feed also selects reg.created_date and reg_selected_services.created_date
#                   (the SQL is re-provisioned from the default at startup - no FOLLOWUP_ORACLE_DETAILEDSTATS_SQL override
#                   is set on this service); the sync stores them; the page query returns them + the classification.
#   Api           : unchanged endpoints (GET /detailed-statistics carries 3 more fields).
#   wwwroot       : Detailed Statistics - "Reg Created" (per Acc No) and "Test Created" (per test) columns, rows flagged
#                   blue (test added within 3 h of the registration) / red (more than 3 h), "Test Addition" badge column,
#                   "Test Addition Status" multi-select filter (Within 3 Hours / More than 3 Hours), exports + help text.
#   NOTE: rows synced before this release have no creation times until their days are synced again (Sync from Oracle
#         on the page, or the nightly job for yesterday).
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
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'DetailedRegistrationCreatedDates' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the DetailedRegistrationCreatedDates migration - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'TestAdditionStatus' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks TestAdditionStatus - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PdfWriter' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll predates the 2026-09-20 release - rebuild from main (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'Within3Hours' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the Test Addition Status UI (Within3Hours) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the Detailed Statistics test-addition UI)."

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
    $dumpFile = "C:\FollowUp\db-backup-detailed-test-addition-$stamp.dump"
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
$backup = "C:\FollowUp\app-backup-detailed-test-addition-$stamp"
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

Write-Host "Starting FollowUp service (applies the DetailedRegistrationCreatedDates migration)..."
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
