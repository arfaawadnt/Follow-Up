# Deploy: Daily-visit document attachments + manual visit record.
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle AND applies a new EF migration on startup:
#   Domain        : VisitAttachment aggregate (private-stored docs, bound to the stable visit id)
#   Application   : upload/serve/bind commands+query, RecordManualVisit command, attachment refs on the
#                     board/transfer/check-in/lifecycle DTOs
#   Infrastructure: LocalAttachmentStorage (PRIVATE volume, config Attachments:Path — NOT the public /uploads),
#                     VisitAttachment repo/config, VisitAttachmentQueries, OperationsQueries enrichment
#   Migration     : 20260906215114_AddVisitAttachment — creates visit_attachment (keyed by the stable visit id,
#                     FK to laboratory, indexes). Applied by MigrateAsync on startup.
#   wwwroot       : Daily record dialog + a new "Record manual visit" dialog get file upload; documents are
#                     viewable (authenticated) on Daily / Transfers / Lab Check-in / Sample Tracking.
#
# Attachment bytes live under C:\FollowUp\app\attachments (auto-created; NOT wiped by this deploy, which only
# mirrors wwwroot). Served only via the authenticated GET /api/v1/daily/attachments/{id}.
#
# Run in an ELEVATED PowerShell. Assumes Release DLLs + Angular bundle are already built this session; -Build to build.
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

$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-visit-attachments-$stamp"
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

Write-Host "Starting FollowUp service (applies migration)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

Start-Sleep -Seconds 5
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
    Write-Host "Verify: Daily Follow-up -> Record visit / Record manual visit -> attach a PDF; the doc shows (view-only) on Daily/Transfers/Lab Check-in/Sample Tracking."
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "Roll back by copying DLLs + wwwroot from $backup back into $app and restarting the service."
}
