# Deploy: Segment monthly target-income bands + income-based monthly auto-assignment.
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies a new EF migration on startup:
#   Domain        : RefItem.TargetIncomeFrom/To (+ SetTargetIncome); Laboratory.ReassignSegment
#   Application   : Setup ref create/update carry the band; SegmentBands matcher; AssignLabSegments command;
#                     ISegmentAssignmentRunner abstraction
#   Infrastructure: SegmentAssignmentRunner (income-based assign), MonthlySegmentAssignmentJob (1st @ 02:00
#                     Cairo), RefItem EF columns, seeder default bands
#   Migration     : 20260906113343_AddSegmentTargetIncome — adds ref_item.target_income_from/to, DROPS the
#                     legacy ck_laboratory_segment (A/B/C-only) CHECK, back-fills default bands
#                     (C 0-3000, B 3001-6000, A 6001-10000, VIP >10000). Applied by MigrateAsync on startup.
#   wwwroot       : Setup > Segments gains From/To inputs + "Run assignment now"
#
# Run in an ELEVATED PowerShell (service stop/start needs admin). Assumes the Release DLLs and Angular bundle
# are already built this session; pass -Build to build them here.
# -------------------------------------------------------------------------------------------------
param([switch]$Build)

$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$repo   = 'D:\App'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$srcWeb = "$repo\src\FollowUp.Api\wwwroot"
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')
$dotnet = 'C:\dotnet\dotnet.exe'
$nodeDir = 'C:\nodejs'

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

# --- Backup current DLLs + wwwroot --------------------------------------------------------------
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-segment-income-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

# --- Stop -> copy -> start (MigrateAsync applies the new migration on startup) -------------------
Write-Host "Stopping FollowUp service..."
if ((Get-Service FollowUp).Status -ne 'Stopped') {
    Stop-Service FollowUp -Force
    (Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

# 'Stopped' can precede the host process fully releasing file handles — wait until a DLL is exclusively
# openable (unlocked) before copying, so the copy can't race the shutdown.
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

Write-Host "Starting FollowUp service (applies migration)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# --- Health check -------------------------------------------------------------------------------
Start-Sleep -Seconds 5
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
    Write-Host "Verify: Setup > Segments shows From/To bands; 'Run assignment now' reassigns labs by last month's income."
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "Roll back by copying DLLs + wwwroot from $backup back into $app and restarting the service."
}
