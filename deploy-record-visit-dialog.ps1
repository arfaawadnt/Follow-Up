# Deploy: Record visit dialog (Dashboard + Daily Follow-up) displays as a proper modal again (main).
# -------------------------------------------------------------------------------------------------
# wwwroot-ONLY release. No DLL changes, no migration, NO service restart: Kestrel serves the new static files the
# moment they land. Users hard-refresh (Ctrl+F5) past the cached index.html.
#   wwwroot : the shared app-record-visit-dialog carried no styles of its own - it relied on the .dlg rule of the page that
#             hosted it, which Angular's style encapsulation never lets through - so the form floated unboxed over the page,
#             taller than the viewport, under the top bar. It now owns its overlay/box styles (centered card, 90vh max
#             height with its own scrollbar, above the top bar).
#
# Run in an ELEVATED PowerShell (C:\FollowUp\app is ACL-protected). Assumes the bundle is built; -Build to rebuild.
# -------------------------------------------------------------------------------------------------
param([switch]$Build)

$ErrorActionPreference = 'Stop'
$app     = 'C:\FollowUp\app'
$repo    = 'D:\App'
$srcWeb  = "$repo\src\FollowUp.Api\wwwroot"
$nodeDir = 'C:\nodejs'
$stamp   = Get-Date -Format 'yyyyMMdd-HHmmss'

$head  = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$dirty = (& git -C $repo status --porcelain -- web) | Where-Object { $_ }
if ($head -ne 'main') { Write-Warning "Checked-out branch is '$head', not 'main'. Merge the PR into main first (prod is built from main)." }
if ($dirty) { Write-Warning "Uncommitted changes under web/:`n$($dirty -join "`n")" }

if ($Build) {
    Write-Host "Building Angular bundle..."
    $env:Path = "$nodeDir;" + $env:Path
    Push-Location "$repo\web"
    try { & "$nodeDir\npm.cmd" run build; if ($LASTEXITCODE -ne 0) { throw "ng build failed (exit $LASTEXITCODE)." } }
    finally { Pop-Location }
}

if (-not (Test-Path "$srcWeb\index.html")) { throw "Missing $srcWeb\index.html - build the Angular bundle first (pass -Build)." }

# CSP fix: strip media="print" onload="this.media='all'" (the app CSP blocks the inline onload). Idempotent.
$index = "$srcWeb\index.html"
$html  = Get-Content $index -Raw
$fixed = $html -replace '\s*media="print"', '' -replace '\s*onload="this\.media=''all''"', ''
if ($fixed -ne $html) {
    [System.IO.File]::WriteAllText($index, $fixed, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Applied CSP stylesheet fix to index.html."
} else { Write-Host "CSP fix already applied (or not needed)." }
if ($fixed -match 'media="print"') { throw "ABORT: CSP fix failed (print-onload still present in index.html)." }

# Payload guard: the shared record-visit dialog chunk must carry its own modal styles.
$ok = @(Get-ChildItem "$srcWeb\*.js" | Where-Object { $c = Get-Content $_.FullName -Raw; $c -match "app-record-visit-dialog" -and $c -match "max-height:90vh" })
if ($ok.Count -eq 0) { throw "ABORT: no bundle file carries the styled record-visit dialog - rebuild (pass -Build)." }
Write-Host "Payload OK."

$backup = "C:\FollowUp\wwwroot-backup-record-visit-dialog-$stamp"
Write-Host "Backing up current wwwroot -> $backup"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
robocopy "$app\wwwroot" $backup /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Mirroring wwwroot (no service restart needed)..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null
if ($LASTEXITCODE -gt 3) { throw "robocopy reported failure (exit $LASTEXITCODE). Restore from $backup." }

$expected = (Get-ChildItem "$srcWeb\main-*.js" | Select-Object -First 1).Name
$served   = [regex]::Match((Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15).Content, 'main-[A-Z0-9]+\.js').Value
if ($served -eq $expected) {
    Write-Host "Live server now serves $served. Backup at $backup"
    Write-Host "Verify (Ctrl+F5): Dashboard or Daily Follow-up -> Record visit: the form opens as a centered white card that fits"
    Write-Host "  the screen (scrolls inside if needed) with Cancel / Confirm visit visible at the bottom."
} else {
    Write-Warning "Server still serves '$served' (expected '$expected'). Check C:\FollowUp\app\wwwroot; roll back from $backup if needed."
}
