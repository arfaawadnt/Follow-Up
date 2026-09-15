# Deploy: Deductions automation - penalties mirror into deductions; one auto Percentage Deal row per area per month
#         (feature/deductions-automation).
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies 1 new EF migration on startup (MigrateAsync):
#   DeductionAutomation : deduction.origin (Manual/AutoPenalty/AutoDeal, default Manual), is_adjusted, system_note,
#                         penalty_record_id (Restrict FK, unique when set), unique (area_id, period_from) for AutoDeal
#                         rows, and ck_deduction_origin mirroring the domain invariant. Existing rows become Manual.
#   Domain        : Deduction origins + FromPenalty / AutoDeal / Recalculate / Adjust; Update refuses automated rows
#   Application   : penalty Create/Update/Delete keep the mirrored deduction in step (same transaction);
#                   UpdateDeduction adjusts automated rows (value + notes + basis); DeleteDeduction refuses penalty mirrors;
#                   RecalculateDeductionsCommand (ManageAccounting) runs the automation now
#   Infrastructure: DeductionAutomationRunner + daily Hangfire job "deductions-automation" (00:30 Cairo, after the
#                   nightly stats pull); DeductionsAsync resolves penalty serials
#   Api           : POST /accounting/deductions/recalculate; DeductionBody.Basis
#   wwwroot       : Deductions page - Status column (Manual / Auto-calculated / Manually adjusted), system details
#                   under Notes, "Penalty #N" on mirrors, locked fields for automated rows, "Recalculate now" button;
#                   exports gain Status + Details columns
#
# First-run effect: the 00:30 job (or "Recalculate now") links EVERY existing penalty record to a new AutoPenalty
# deduction on its lab's area and creates this month's AutoDeal row for each area with an active deal.
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
# Payload guards (refuse a stale build).
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'DeductionAutomation' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the DeductionAutomation migration/runner - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'AutoPenalty' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks DeductionOrigin - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'deductions/recalculate' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the Deductions 'Recalculate now' action - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the new Deductions page)."

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
    $dumpFile = "C:\FollowUp\db-backup-deductions-automation-$stamp.dump"
    Write-Host "Backing up database '$pgDb' on ${pgHost}:$pgPort -> $dumpFile (takes ~3 minutes; the service keeps running meanwhile)"
    $env:PGPASSWORD = $kv['password']
    try {
        & $pgDump -h $pgHost -p $pgPort -U $pgUser -d $pgDb -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE). Aborting before any change." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $size = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
    Write-Host "Database backup OK ($size MB). Restore with: pg_restore -h $pgHost -p $pgPort -U $pgUser -d $pgDb --clean --if-exists $dumpFile"
} else { Write-Warning "Skipping database backup (-SkipDbBackup). 1 migration will apply on startup with no dump to fall back on." }

# ---- App backup ----------------------------------------------------------------------------------------------
$backup = "C:\FollowUp\app-backup-deductions-automation-$stamp"
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

Write-Host "Starting FollowUp service (applies 1 migration; registers the daily deductions-automation job)..."
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
    Write-Host "Verify (Ctrl+F5): Accounting -> Deductions -> 'Recalculate now': every existing penalty gains a mirrored row"
    Write-Host "  (Reason 'Penalty #N', details under Notes, status Auto-calculated) and each deal area gets this month's"
    Write-Host "  Percentage Deal row. Edit an automated row: area/reason/period locked; changing the value marks it Manually adjusted."
    Write-Host "  Record a penalty on the Penalty Statement: its deduction appears immediately; delete it and the deduction goes too."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
