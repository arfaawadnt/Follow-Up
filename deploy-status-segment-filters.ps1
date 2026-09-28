# Deploy: Detailed Statistics sample / test status + Segment filters (feat/status-segment-filters)
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed FollowUp DLLs + the Angular bundle. NO migration, NO new packages (the SkiaSharp assemblies, native
# libraries and merged deps.json of the 2026-09-20 release stay exactly as they are).
#   Application   : AreaStatDto gains Segment (the lab segment as an extra grain of the area rollup).
#   Infrastructure: AreaStatsQueries groups by (date, governorate, city, area, branch, segment).
#   wwwroot       : Detailed Statistics - Sample Status (1 ordered / 2 collected / 3 received) and Test Status
#                   (1-2 ordered / 3 completed / 4 reviewed / 5 verified) columns per test line + two multi-select filters
#                   (default All), both in the Excel / PDF exports; Area Statistics - Segment multi-select filter;
#                   Lab Statistics - the Segment filter is now multi-select.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to build.
# Nothing in the database changes, so no pg_dump is taken; the replaced DLLs + wwwroot are backed up beside the app folder.
# -------------------------------------------------------------------------------------------------
param([switch]$Build)

$ErrorActionPreference = 'Stop'
$repo   = 'D:\App'
$app    = 'C:\FollowUp\app'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$srcWeb = "$repo\src\FollowUp.Api\wwwroot"
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')
$dotnet = 'C:\dotnet\dotnet.exe'
$nodeDir = 'C:\nodejs'
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
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'PdfWriter' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll predates the 2026-09-20 release - rebuild from main (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'st_verified' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the status labels (st_verified) - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the status / segment filters)."

# ---- Backup of the replaced files ----
$backup = "C:\FollowUp\app-backup-status-filters-$stamp"
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

Write-Host "Starting FollowUp service (no migration)..."
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
    Write-Host "  1. Detailed Statistics: Sample Status and Test Status columns per test line; filter both (multi-select, default All)."
    Write-Host "  2. Area Statistics: Segment filter (multi-select)."
    Write-Host "  3. Lab Statistics: the Segment filter is now multi-select."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 90s."
    Write-Warning "Check C:\FollowUp\app\logs for a startup failure."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, start the service."
}
