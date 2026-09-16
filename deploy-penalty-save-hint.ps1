# Deploy: Penalty Statement - the Record penalty dialog names the missing fields beside Save (feature/real-income-sheet).
# -------------------------------------------------------------------------------------------------
# wwwroot-ONLY release. No DLL changes, no migration, NO service restart: Kestrel serves the new static files the
# moment they land. Users hard-refresh (Ctrl+F5) past the cached index.html.
#   wwwroot : Record penalty - Save is always clickable; the starred fields still empty/invalid are listed in red beside
#             it ("Missing: ...") and Save shows the same list as a warning instead of doing nothing. Lab Request needs
#             no User. (Follow-up to the "Lab Request penalty not saved" report: no save request ever reached the server,
#             i.e. the button was disabled by an unfilled field the dialog did not point out.)
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

# Payload guard: the Penalty Statement chunk must carry the missing-fields hint.
$ok = @(Get-ChildItem "$srcWeb*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match "Please fill: " })
if ($ok.Count -eq 0) { throw "ABORT: no bundle file carries the missing-fields hint - rebuild (pass -Build)." }
Write-Host "Payload OK."

$backup = "C:\FollowUp\wwwroot-backup-row-serial-$stamp"
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
    Write-Host "Verify (Ctrl+F5): Accounting -> Penalty Statement -> Record penalty: pick User Type = Lab Request; a red 'Missing: ...' line"
    Write-Host "  beside Save names whatever is still empty; once it disappears, Save records the penalty."
} else {
    Write-Warning "Server still serves '$served' (expected '$expected'). Check C:\FollowUp\app\wwwroot; roll back from $backup if needed."
}
