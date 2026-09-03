# Deploy: Area Statistics page (/area-statistics).
# -------------------------------------------------------------------------------------------------
# NO new migration and NO new Oracle feed. The page re-aggregates the EXISTING daily_lab_statistic
# rows by each lab's stamped Governorate -> Area, so the nightly labstats-sync (00:05 Cairo) already
# keeps its data current (daily auto-sync is covered for free). Manual "Sync from Oracle" on the page
# reuses RunLabStatsAsync (same runner as the Lab Statistics page).
#
# Net source changes vs running prod (all 4 managed DLLs ship, same shape as deploy-teststat-bytype.ps1):
#   Domain        : Privileges.cs  -> new ViewAreaStats privilege (const + All set + ViewReports cross-grant)
#   Application   : Features/AreaStats/AreaStats.cs (new) -> query + scoped handler + sync command
#   Infrastructure: AreaStatsQueries (read model), DI registration,
#                   DatabaseSeeder -> idempotently backfills the built-in Admin role to Privileges.All
#                   (so ViewAreaStats reaches Admin on the first boot after this deploy; writes to the DB)
#   Api           : /area-statistics + /area-statistics/sync endpoints
#   wwwroot       : new Angular areastats page + route + nav entry + EN/AR translations
#
# POST-DEPLOY:
#   * Admin and any role with ViewReports (e.g. OperationsManager) get the page automatically.
#     Other custom roles need ViewAreaStats granted via the Roles page before the menu item appears.
#   * No re-sync needed -the page reads the lab-stats history you already maintain.
#
# Run in an ELEVATED PowerShell (the service stop/start needs admin). By default this builds Release
# DLLs + the Angular bundle first; pass -SkipBuild if you have already built them this session.
# -------------------------------------------------------------------------------------------------
param([switch]$SkipBuild)

$ErrorActionPreference = 'Stop'
$app     = 'C:\FollowUp\app'
$repo    = 'D:\App'
$srcBin  = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$srcWeb  = "$repo\src\FollowUp.Api\wwwroot"          # Angular 'application' builder outputs straight here
$dlls    = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')
$dotnet  = 'C:\dotnet\dotnet.exe'
$nodeDir = 'C:\nodejs'

# --- 1. Build (Release DLLs + Angular bundle), unless -SkipBuild --------------------------------
if (-not $SkipBuild) {
    Write-Host "Building Release DLLs (FollowUp.Api + dependency graph)..."
    & $dotnet build "$repo\src\FollowUp.Api\FollowUp.Api.csproj" -c Release --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

    Write-Host "Building Angular bundle (outputs to $srcWeb)..."
    $env:Path = "$nodeDir;" + $env:Path
    Push-Location "$repo\web"
    try {
        & "$nodeDir\npm.cmd" run build
        if ($LASTEXITCODE -ne 0) { throw "ng build failed (exit $LASTEXITCODE)." }
    } finally { Pop-Location }

    # CSP fix: the fresh build re-emits the stylesheet <link> with `media="print" onload="this.media='all'"`,
    # whose inline onload the app's CSP (script-src 'self') blocks -> the app renders unstyled. Strip both
    # attributes so the sheet loads normally (see the CSS/CSP gotcha in the deploy notes).
    $index = "$srcWeb\index.html"
    if (Test-Path $index) {
        $html = Get-Content $index -Raw
        $fixed = $html -replace '\s*media="print"', '' -replace '\s*onload="this\.media=''all''"', ''
        if ($fixed -ne $html) {
            # UTF-8 WITHOUT BOM (Set-Content -Encoding UTF8 on PS 5.1 would prepend a BOM); match how Angular writes it.
            [System.IO.File]::WriteAllText($index, $fixed, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "Applied CSP stylesheet fix to index.html."
        } else {
            Write-Host "CSP stylesheet fix not needed (pattern not present)."
        }
    }
}

foreach ($d in $dlls) {
    if (-not (Test-Path "$srcBin\$d")) { throw "Missing $srcBin\$d -build first (omit -SkipBuild)." }
}
if (-not (Test-Path "$srcWeb\index.html")) { throw "Missing $srcWeb\index.html -build the Angular bundle first." }

# --- 2. Backup current DLLs + wwwroot -----------------------------------------------------------
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-areastats-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null

Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

# --- 3. Stop -> copy -> start -------------------------------------------------------------------
Write-Host "Stopping FollowUp service..."
Stop-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

Write-Host "Copying new DLLs..."
foreach ($d in $dlls) { Copy-Item "$srcBin\$d" $app -Force }

Write-Host "Mirroring new wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service (seeder backfills Admin privileges on startup; no migration pending)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# --- 4. Health check ----------------------------------------------------------------------------
Start-Sleep -Seconds 4
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
    Write-Host "Verify: log in, open the 'Area Statistics' menu item, pick a Reference Month, and confirm the grid + green/red flags."
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "Roll back by copying DLLs + wwwroot from $backup and restarting the service."
}
