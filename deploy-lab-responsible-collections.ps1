# Deploy: Lab Responsible (was Area Responsible) + collections per rep with per-rep amounts
#         (feature/lab-responsible-rep-collections).
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies 1 new EF migration on startup (MigrateAsync):
#   LabResponsibleAndRepCollections :
#     - representative.type 'AreaResponsible' rows are RENAMED to 'LabResponsible' (same id 5); ck_representative_type widened
#       to the new name list. Nothing to re-enter: existing responsibles keep working under the new type.
#     - laboratory.responsible_rep_id (Restrict FK to representative, indexed) is added; each area's former responsible is
#       CARRIED to the labs of that area (match on area name, case/space-insensitive), then area.area_responsible_id is dropped.
#     - collection.laboratory_id is DROPPED (a collection belongs to its reps, not a lab) and rep_ids becomes rep_shares
#       [{RepId, Amount}] - existing rows converted in place (single rep = total; several reps = equal split). Prod has
#       0 collections today, so this is a schema-only step there.
#   Domain        : RepresentativeType.LabResponsible; Laboratory.ResponsibleRepId/AssignResponsible; Area loses its responsible;
#                   Collection.Shares (per-rep amounts, sum = cash + bank for a group) / ShareOf; treasury mirror note lists shares
#   Application   : lab Create/Update accept ResponsibleRepId (type-bound); area commands lose AreaResponsibleId;
#                   collection commands take Shares (only LabResponsible reps; group shares must sum to the total);
#                   ICollectionRouting decides the treasury branch (rep Branch, else the branch of the labs the rep is responsible for)
#   Infrastructure: EF mappings + migration; CollectionRouting; collections scoped by the reps; rep statement credits the rep's share
#   Api           : /labs bodies gain responsibleRepId; /setup/areas bodies lose areaResponsibleId;
#                   /accounting/collections bodies take shares[] instead of laboratoryId + repIds; GET loses laboratoryId
#   wwwroot       : Lab create/detail/list - "Lab Responsible" picker/column/filter; Setup -> Areas loses Area Responsible;
#                   Reps form type "LabResponsible"; Collection page - no lab, reps limited to Lab Responsibles, per-rep amounts on
#                   a Group collection (sum must equal cash + bank), grid + exports show the shares
#
# TO KNOW AFTER THIS RELEASE:
#   - Reps typed "Area Responsible" now show as "LabResponsible"; assign them to their labs (Lab detail -> Lab Responsible) if the
#     area-name carry-over did not cover a lab.
#   - Treasury routing for a collection now follows the rep: set the rep's Branch on the rep form, or make the rep the
#     Lab Responsible of labs in the branch; a collection whose reps resolve to no branch stays unplaced until then
#     (Treasury Account -> "Sync collections" links it afterwards).
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
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'LabResponsibleAndRepCollections' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the LabResponsibleAndRepCollections migration - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'TreasuryGrant' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks TreasuryGrant - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'treasury/grants' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the treasury rights panel (treasury/grants) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the treasury rights UI)."

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
    $dumpFile = "C:\FollowUp\db-backup-treasury-sync-$stamp.dump"
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
$backup = "C:\FollowUp\app-backup-treasury-sync-$stamp"
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

Write-Host "Starting FollowUp service (applies 1 migration)..."
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
    Write-Host "  1. Reps: reps formerly typed Area Responsible now read LabResponsible; Lab detail -> Lab Responsible to assign per lab."
    Write-Host "  2. Accounting -> Collection: Record collection has no Lab; the rep pickers list Lab Responsibles only; a Group asks an amount per rep."
    Write-Host "  3. Accounting -> Treasury Account -> 'Sync collections' (mirrors any cash collection whose rep now resolves to a treasury branch)."
    Write-Host "  4. Labs list: new 'Lab Responsible' column + filter; Setup -> Areas no longer shows Area Responsible."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
