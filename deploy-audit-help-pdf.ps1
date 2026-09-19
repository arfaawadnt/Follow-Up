# Deploy: Page help, full audit trail module, PDF email attachments (feat/help-audit-pdf)
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle + NEW runtime assemblies. NO database migration (the audit trail table
# audit_entry already exists; this release adds the per-record read side, the privilege and the UI).
#   Privileges    : NEW ViewAuditTrail (implied by ManageUsers; the built-in Admin role is backfilled with every privilege
#                   at startup). Grant it on the Roles page to any role that should see the change-log buttons / Audit Trail page.
#   Application   : GetAuditQuery gains entityId / from / to and returns the changed fields (AuditDiff); NEW GetEntityAuditQuery
#                   (one record's history) and GetAuditFacetsQuery; audit reads need ViewAuditTrail instead of ManageUsers.
#   Infrastructure: AuditQueries.ForEntityAsync / FacetsAsync; PdfWriter (SkiaSharp + HarfBuzz: A4 landscape, Arabic shaped,
#                   colour flags, wide tables split into column groups); the email reports attach a PDF beside every Excel file.
#   NEW DLLs      : SkiaSharp.dll, SkiaSharp.HarfBuzz.dll, HarfBuzzSharp.dll + runtimes\win-x64\native\libSkiaSharp.dll,
#                   libHarfBuzzSharp.dll, and the updated FollowUp.Api.deps.json (the host resolves the new assemblies
#                   through it - without it the email job fails with FileNotFoundException).
#   Api           : GET /audit (entityId, from, to), GET /audit/entity?entity&entityId, GET /audit/facets.
#   wwwroot       : ? help icon in the header -> page help popup (purpose, business, workflow progress bar, how it works,
#                   tips, privileges; EN/AR) for every page; a change-log (clock) button on the rows of every grid and on the lab /
#                   rep detail pages; rebuilt Audit Trail page (date range, entity, user, action, record id, expandable
#                   before -> after diffs, paging, Excel); Roles page row "Audit Trail".
#
# TO KNOW AFTER THIS RELEASE:
#   - Non-admin roles see the change-log buttons and the Audit Trail page only after ViewAuditTrail is granted (Roles page).
#   - Existing audit rows are shown with their diffs computed from the stored snapshots; nothing is rewritten.
#   - The email PDF uses Arial from C:\Windows\Fonts (Arabic glyphs); Segoe UI is the fallback.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to build.
# Takes a pg_dump (custom format) of the live DB before touching anything; -SkipDbBackup to skip (no migration this release).
# -------------------------------------------------------------------------------------------------
param([switch]$Build, [switch]$SkipDbBackup)

$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$repo   = 'D:\App'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$srcWeb = "$repo\src\FollowUp.Api\wwwroot"
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll',
            'SkiaSharp.dll', 'SkiaSharp.HarfBuzz.dll', 'HarfBuzzSharp.dll', 'FollowUp.Api.deps.json')
$native = @('libSkiaSharp.dll', 'libHarfBuzzSharp.dll')  # under runtimes\win-x64\native (SkiaSharp.NativeAssets.Win32)
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
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PdfWriter' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the PDF writer - rebuild (pass -Build)." }
foreach ($n in $native) { if (-not (Test-Path "$srcBin\runtimes\win-x64\native\$n")) { throw "ABORT: missing native library $n under $srcBin\runtimes\win-x64\native - restore/rebuild (pass -Build)." } }
if (-not (Select-String -Path "$srcBin\FollowUp.Api.deps.json" -Pattern 'SkiaSharp.HarfBuzz' -Quiet)) { throw "ABORT: FollowUp.Api.deps.json does not list SkiaSharp.HarfBuzz - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Domain.dll" -Pattern 'ViewAuditTrail' -Quiet)) { throw "ABORT: FollowUp.Domain.dll lacks the ViewAuditTrail privilege - rebuild (pass -Build)." }
if (-not (Select-String -Path "$srcBin\FollowUp.Application.dll" -Pattern 'GetEntityAuditQuery' -Quiet)) { throw "ABORT: FollowUp.Application.dll lacks the per-record audit query - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'help_workflow' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the page help dialog (help_workflow) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the help / audit UI)."

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
} else { Write-Warning "Skipping database backup (-SkipDbBackup). No migration this release; the dump is a safety net only." }

# ---- App backup ----------------------------------------------------------------------------------------------
$backup = "C:\FollowUp\app-backup-statement-rework-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
foreach ($n in $native) { if (Test-Path "$app\runtimes\win-x64\native\$n") { Copy-Item "$app\runtimes\win-x64\native\$n" $backup -Force } }
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
New-Item -ItemType Directory -Path "$app\runtimes\win-x64\native" -Force | Out-Null
foreach ($n in $native) { Copy-Item "$srcBin\runtimes\win-x64\native\$n" "$app\runtimes\win-x64\native" -Force }
Write-Host "Mirroring wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service (no migration this release)..."
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
    Write-Host "  1. Any page: the ? icon in the header opens the page help (purpose, business, workflow bar, how it works, tips, privileges)."
    Write-Host "  2. Any grid: the clock (change log) button on a row opens its change log (who created / changed it, when, before -> after)."
    Write-Host "  3. System and Admin -> Audit Trail: date range, entity, user, action, record id; expand a row for the diff."
    Write-Host "  4. Roles: grant 'Audit Trail' (ViewAuditTrail) to the roles that should see the log buttons."
    Write-Host "  5. Email Reports -> Send now on a subscription: the email carries a PDF beside each Excel attachment."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a migration or startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, restore the DB dump if the migration partially applied, start the service."
}
