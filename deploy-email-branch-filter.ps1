# Deploy: Email Reports — "Serving branch" filter on scheduled statistics reports (feature/email-reports-serving-branch).
# -------------------------------------------------------------------------------------------------
# Ships the 4 managed DLLs + the Angular bundle. NO migration (the filter lives inside the existing jsonb
# filters payload), so no DB backup is taken. A service restart IS required (Infrastructure.dll changes).
#   Infrastructure: StatsEmailRunner applies the saved `branches` filter to the Lab and Area statistics sections
#                   (the labs' operator-managed serving branch). Older subscriptions without the member are unchanged.
#   Domain / Application / Api: rebuilt from the same tree, no functional change (shipped together to keep the
#                   four assemblies in lock-step, per the TypeLoadException lesson).
#   wwwroot       : Email Reports editor gains a "Serving branch" multi-select (Setup › Branches items).
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
# Payload guards: the runner must carry the Branches filter and the bundle the new field.
if (-not (Select-String -Path "$srcBin\FollowUp.Infrastructure.dll" -Pattern 'Branches' -Quiet)) { throw "ABORT: FollowUp.Infrastructure.dll lacks the Branches filter - rebuild (pass -Build)." }
$chunk = Get-ChildItem "$srcWeb\*.js" | Where-Object { (Get-Content $_.FullName -Raw) -match 'branches:\[\]' -and (Get-Content $_.FullName -Raw) -match 'serving_branch' } | Select-Object -First 1
if (-not $chunk) { throw "ABORT: no bundle file carries the Email Reports serving-branch field - rebuild (pass -Build)." }
Write-Host "Payload OK ($($chunk.Name) carries the new filter)."

$backup = "C:\FollowUp\app-backup-email-branch-$stamp"
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

Write-Host "Starting FollowUp service (no migration to apply)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

$healthy = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try { $r = Invoke-WebRequest -Uri 'http://localhost:5088/healthz/ready' -UseBasicParsing -TimeoutSec 5; if ($r.StatusCode -eq 200) { $healthy = $true; break } } catch { }
    if ((Get-Service FollowUp).Status -ne 'Running') { break }
}
if ($healthy) {
    Write-Host "Service is up and healthy. Backup at $backup"
    Write-Host "Verify (Ctrl+F5): System & Admin -> Email Reports -> edit a scheduled report -> Filters now include 'Serving branch'."
    Write-Host "  Pick a branch, Save, then 'Send now': the Lab Statistics section lists only labs served by that branch."
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 60s."
    Write-Warning "Roll back: stop the service, copy DLLs + wwwroot from $backup back into $app, start the service."
}
