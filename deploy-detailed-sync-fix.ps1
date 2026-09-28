# Deploy: Detailed Statistics manual sync fix (fix/detailed-stats-manual-sync)
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed FollowUp DLLs only. NO migration, NO wwwroot change, NO new packages (the SkiaSharp assemblies,
# native libraries and merged deps.json of the 2026-09-20 release stay exactly as they are).
#   Infrastructure: OracleSyncRunner.RunDetailedStatsAsync joins an ambient transaction (the manual sync arrives through
#                   SyncDetailedStatsCommand, which TransactionBehavior already wraps) instead of opening a nested one -
#                   Npgsql refused that with "The connection is already in a transaction" (HTTP 500 on the Detailed
#                   Statistics "Sync" button, 2026-09-22). The scheduled nightly sync was never affected.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs are already built this session; -Build to build.
# Nothing in the database changes, so no pg_dump is taken; the replaced DLLs are backed up beside the app folder.
# -------------------------------------------------------------------------------------------------
param([switch]$Build)

$ErrorActionPreference = 'Stop'
$repo   = 'D:\App'
$app    = 'C:\FollowUp\app'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')
$dotnet = 'C:\dotnet\dotnet.exe'
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'

$head  = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$dirty = (& git -C $repo status --porcelain -- src web) | Where-Object { $_ }
if ($head -ne 'main') { Write-Warning "Checked-out branch is '$head', not 'main'. Merge the PR into main first (prod is built from main)." }
if ($dirty) { Write-Warning "Uncommitted changes under src/ or web/:`n$($dirty -join "`n")" }

if ($Build) {
    Write-Host "Building Release DLLs..."
    & $dotnet build "$repo\FollowUp.sln" -c Release --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
}

foreach ($d in $dlls) { if (-not (Test-Path "$srcBin\$d")) { throw "Missing $srcBin\$d - build first (pass -Build)." } }
# Payload guards (refuse a stale build). Member names are UTF-8 metadata and searchable.
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PdfWriter' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll predates the 2026-09-20 release - rebuild from main (pass -Build)." }
if ((Get-Item "$srcBin\FollowUp.Infrastructure.dll").LastWriteTime -lt (Get-Date).AddHours(-24)) { Write-Warning "FollowUp.Infrastructure.dll is older than 24 h - make sure it carries the sync fix (pass -Build to rebuild)." }
Write-Host "Payload OK."

# ---- Backup of the replaced DLLs ----
$backup = "C:\FollowUp\app-backup-sync-fix-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
Write-Host "Backed up current DLLs -> $backup"

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

Write-Host "Starting FollowUp service (no migration, no bundle change)..."
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
    Write-Host "Next: Detailed Statistics -> Sync for a date range now returns the synced line count instead of a 500."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a startup failure."
    Write-Warning "Roll back: stop the service, copy the DLLs from $backup back into $app, start the service."
}
