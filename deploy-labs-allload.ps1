# Deploy: Labs page "load all + fix counts" (wwwroot-only; no DLL, no migration, no service restart).
# Frontend already built to src/FollowUp.Api/wwwroot with the CSP stylesheet fix applied.
$ErrorActionPreference = 'Stop'
$src = 'D:\App\src\FollowUp.Api\wwwroot'
$dst = 'C:\FollowUp\app\wwwroot'
$bak = "C:\FollowUp\wwwroot-backup-labs-allload-$(Get-Date -Format yyyyMMdd-HHmmss)"

# Sanity: the built index.html must carry the plain stylesheet (CSP fix), not the print-onload variant.
$idx = Get-Content "$src\index.html" -Raw
if ($idx -match 'media="print"') { throw "ABORT: CSP fix missing in built index.html (print-onload present)." }
if ($idx -notmatch '<link rel="stylesheet" href="styles-[^"]+">') { throw "ABORT: no plain stylesheet link in built index.html." }
Write-Host "CSP check OK."

Write-Host "Backing up live wwwroot -> $bak"
robocopy $dst $bak /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Backup robocopy failed (exit $LASTEXITCODE)." }
Write-Host "Backup done (exit $LASTEXITCODE)."

Write-Host "Mirroring new build -> live wwwroot"
robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Deploy robocopy failed (exit $LASTEXITCODE)." }
Write-Host "Deploy done (exit $LASTEXITCODE)."

# Verify the live index.html and bundle.
$live = Get-Content "$dst\index.html" -Raw
$ok = ($live -match '<link rel="stylesheet" href="styles-[^"]+">') -and ($live -notmatch 'media="print"')
Write-Host ("Live index CSP-clean: " + $ok)
Write-Host ("Live main bundle: " + (Get-ChildItem "$dst\main-*.js").Name)
Write-Host "Kestrel serves the new static files immediately. Hard-refresh (Ctrl+F5) the Labs page."
