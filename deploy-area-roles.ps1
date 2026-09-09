# Deploy: Audit remediation (cycle 3, PR #6) + Area management roles / daily follow-up parity (PR #7).
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies 11 new EF migrations on startup (MigrateAsync):
#   AddStatsEmailSubscriptionScope, AddOutsourceConcurrencyToken, RestrictLabCascadeDeletes,
#   GrantStatsWritePrivileges, AddDeliveryLogContent, RestrictCityAreaCascade, UniqueOracleSourceCode,
#   AddOutsourceSegmentChecks, IdempotencyCreatedAtIndex, AddStatsSyncStatus, AreaManagementRoles.
#   Domain        : RepresentativeType AreaResponsible/AreaManager; Area.AreaManagerId/AreaResponsibleId
#   Application   : area role validation (exists -> in scope -> matching type); collector-must-belong-to-lab guard
#   Infrastructure: Restrict FKs area->representative; widened ck_representative_type; remediation fixes
#   Api           : forwarded-headers (loopback-only default), Npgsql tracing, area body ids
#   wwwroot       : Setup/Areas manager+responsible pickers; shared record-visit dialog (daily + dashboard);
#                   lab-bound Collector Rep picker; EN/AR labels
#
# Startup side effects to know about (both idempotent):
#   * SecretsReencryptor encrypts oracle_config.connection_string and smtp_config.password at rest under a key
#     derived from FOLLOWUP_AUTH_SECRET (FOLLOWUP_SECRET_KEY overrides). Rolling back to the OLD DLLs after this
#     runs leaves the SMTP password unreadable by the old code (re-enter it in Mail Gateway); the Oracle string
#     is re-provisioned from FOLLOWUP_ORACLE on every boot, so it self-heals.
#   * The seeder backfills the built-in Admin role to Privileges.All.
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

# Sanity: the source tree should be the merged main (prod is always built from main — see the 2026-09-04 regression).
$head   = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$dirty  = (& git -C $repo status --porcelain -- src web) | Where-Object { $_ }
if ($head -ne 'main') { Write-Warning "Checked-out branch is '$head', not 'main'. Merge PRs #6/#7 into main first (git checkout main; git merge --ff-only feature/area-management-roles; git push)." }
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
# The payload must actually carry the feature (guards against deploying a stale Release build).
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'AreaManagementRoles' -Quiet)) {
    throw "ABORT: FollowUp.Infrastructure.dll does not contain the AreaManagementRoles migration - rebuild (pass -Build)."
}

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
    $dumpFile = "C:\FollowUp\db-backup-area-roles-$stamp.dump"
    Write-Host "Backing up database '$pgDb' on ${pgHost}:$pgPort -> $dumpFile"
    $env:PGPASSWORD = $kv['password']
    try {
        & $pgDump -h $pgHost -p $pgPort -U $pgUser -d $pgDb -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE). Aborting before any change." }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $size = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
    Write-Host "Database backup OK ($size MB). Restore with: pg_restore -h $pgHost -p $pgPort -U $pgUser -d $pgDb --clean --if-exists $dumpFile"
} else { Write-Warning "Skipping database backup (-SkipDbBackup). 11 migrations will apply on startup with no dump to fall back on." }

# ---- App backup ----------------------------------------------------------------------------------------------
$backup = "C:\FollowUp\app-backup-area-roles-$stamp"
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

Write-Host "Starting FollowUp service (applies 11 migrations + secret re-encryption + admin privilege backfill)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# Migrations run before Kestrel listens; poll the health endpoint rather than a fixed sleep.
$healthy = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri 'http://localhost:5088/healthz/ready' -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    if ((Get-Service FollowUp).Status -ne 'Running') { break }
}
if ($healthy) {
    Write-Host "Service is up and healthy. App backup at $backup"
    Write-Host "Verify: Reps -> New rep offers Area Responsible / Area Manager types."
    Write-Host "        Setup -> Areas: create + inline edit show searchable Area Manager / Area Responsible pickers (type-bound)."
    Write-Host "        Dashboard -> Record visit opens the same dialog as Daily Follow-up (attachments, suggested count, lab-bound collectors)."
    Write-Host "        Daily Follow-up -> Collector Rep lists only the lab's assigned collectors (all, with a hint, when none assigned)."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 60s."
    Write-Warning "Check the Application event log / service logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if migrations partially applied, start the service."
}
