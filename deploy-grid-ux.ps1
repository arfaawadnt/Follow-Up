# Deploy: frozen grid headers + arrow-key grid navigation on every page (feature/grid-frozen-header-keys).
# -------------------------------------------------------------------------------------------------
# wwwroot-ONLY release. No DLL changes, no migration, NO service restart: Kestrel serves the new static files the
# moment they land. Users hard-refresh (Ctrl+F5) past the cached index.html.
#   wwwroot : every data table is wrapped in .grid-scroll (own scroll region, header row frozen at its top;
#             stats pages keep their frozen first column); GridKeyboardNavService (started by the shell) gives
#             every grid arrow-key navigation (↑↓←→, Home/End, PageUp/Down, Enter/Space, Esc from an input).
#
# The C# side of this change is a test-only ratchet (ArchitectureTests: GridConventionTests) — nothing to ship.
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

# The payload must carry this change: the global stylesheet has the .grid-scroll rules and the main bundle the nav service.
$css = Get-ChildItem "$srcWeb\styles-*.css" | Select-Object -First 1
if (-not $css -or -not ((Get-Content $css.FullName -Raw) -match '\.grid-scroll')) { throw "ABORT: styles bundle lacks .grid-scroll - rebuild (pass -Build)." }
$main = Get-ChildItem "$srcWeb\main-*.js" | Select-Object -First 1
if (-not $main -or -not ((Get-Content $main.FullName -Raw) -match 'gk-cell')) { throw "ABORT: main bundle lacks GridKeyboardNavService - rebuild (pass -Build)." }
Write-Host "Payload OK: $($css.Name) + $($main.Name) carry the grid UX."

$backup = "C:\FollowUp\wwwroot-backup-grid-ux-$stamp"
Write-Host "Backing up current wwwroot -> $backup"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
robocopy "$app\wwwroot" $backup /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Mirroring wwwroot (no service restart needed)..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null
if ($LASTEXITCODE -gt 3) { throw "robocopy reported failure (exit $LASTEXITCODE). Restore from $backup." }

# Prove the live server is already serving the new bundle.
$served = [regex]::Match((Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15).Content, 'main-[A-Z0-9]+\.js').Value
if ($served -eq $main.Name) {
    Write-Host "Live server now serves $served. Backup at $backup"
    Write-Host "Verify (Ctrl+F5): open Labs -> scroll inside the grid: the header row stays put. Click a cell or Tab into the"
    Write-Host "  grid, then use the arrow keys / Home / End / PageDown; Enter opens a lab (clickable row). Same on every page."
} else {
    Write-Warning "Server still serves '$served' (expected '$($main.Name)'). Check C:\FollowUp\app\wwwroot; roll back from $backup if needed."
}
