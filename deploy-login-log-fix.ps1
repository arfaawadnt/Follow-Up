# Deploy: stop logging expected 4xx (wrong-password 401, 404) as "responded 500" in the request log.
# -------------------------------------------------------------------------------------------------
# Api-only, SINGLE-DLL deploy. Moves ExceptionHandlingMiddleware to just inside UseSerilogRequestLogging
# so Serilog observes the mapped 4xx status instead of the in-flight exception (commit 1964c50).
#   Ships       : FollowUp.Api.dll ONLY
#   No migration: no schema change -> no DB backup taken
#   No wwwroot  : server-side only, no client change
#
# Safe as a single-DLL swap: the only source change since the deployed build (8c4e8ac) is
# src/FollowUp.Api/Program.cs (the composition root). No public type or interface changed, so the
# already-deployed Domain/Application/Infrastructure DLLs bind unchanged (assembly version 1.0.0.0).
# This is NOT like a stats change that also moves an Application interface (which needs the 4-DLL set).
#
# Run in an ELEVATED PowerShell. Assumes the Release Api DLL is already built this session; -Build to build.
# The service stop/start must be user-run (auto-mode blocks Stop/Start-Service).
# -------------------------------------------------------------------------------------------------
param([switch]$Build)

$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$repo   = 'D:\App'
$srcBin = "$repo\src\FollowUp.Api\bin\Release\net8.0"
$dll    = 'FollowUp.Api.dll'
$dotnet = 'C:\dotnet\dotnet.exe'
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'

# The deployed build is main @ 8c4e8ac; this fix is 8c4e8ac + Program.cs only. Building from a branch that is
# strictly ahead of deployed main by just this commit loses nothing, but flag any drift so it's a conscious call.
$head  = (& git -C $repo rev-parse --abbrev-ref HEAD).Trim()
$nonApi = (& git -C $repo diff --name-only 8c4e8ac..HEAD -- src/) | Where-Object { $_ -and $_ -notlike 'src/FollowUp.Api/*' }
if ($nonApi) { throw "ABORT: source changed outside the Api project since the deployed build (8c4e8ac):`n$($nonApi -join "`n")`nThis is no longer a single-DLL deploy - use a 4-DLL script." }
if ($head -ne 'main') { Write-Warning "Building from branch '$head' (not 'main'). Fine here - it is deployed-main + only the Program.cs fix. Merge to main after deploying to keep prod == main." }

if ($Build) {
    Write-Host "Building Release Api DLL..."
    & $dotnet build "$repo\src\FollowUp.Api\FollowUp.Api.csproj" -c Release --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }
}

if (-not (Test-Path "$srcBin\$dll")) { throw "Missing $srcBin\$dll - build first (pass -Build)." }

# Backup the current DLL before swapping.
$backup = "C:\FollowUp\app-backup-login-log-fix-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
if (Test-Path "$app\$dll") { Copy-Item "$app\$dll" $backup -Force }
Write-Host "Backed up current $dll -> $backup"

Write-Host "Stopping FollowUp service..."
if ((Get-Service FollowUp).Status -ne 'Stopped') {
    Stop-Service FollowUp -Force
    (Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

# 'Stopped' can precede the host process releasing file handles - wait until the DLL is exclusively openable.
$unlocked = $false
for ($i = 0; $i -lt 40; $i++) {
    try { $fs = [System.IO.File]::Open("$app\$dll", 'Open', 'ReadWrite', 'None'); $fs.Close(); $unlocked = $true; break }
    catch { Start-Sleep -Milliseconds 1000 }
}
if (-not $unlocked) { throw "$dll still locked after 40s - a host process is holding it; stop it and re-run." }

Write-Host "Copying $dll..."
Copy-Item "$srcBin\$dll" $app -Force

Write-Host "Starting FollowUp service..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# Poll the ready endpoint (checks the DB) rather than a fixed sleep.
$healthy = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri 'http://localhost:5088/healthz/ready' -UseBasicParsing -TimeoutSec 5
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
    if ((Get-Service FollowUp).Status -ne 'Running') { break }
}
if ($healthy) {
    Write-Host "Service is up and healthy. Backup at $backup"
    Write-Host "Verify: a wrong-password login now logs 'responded 401' at Information with NO 'responded 500'."
    Write-Host "  Quick check (PowerShell):"
    Write-Host "    Select-String -Path 'C:\FollowUp\app\logs\followup-$(Get-Date -Format yyyyMMdd).log' -Pattern 'auth/login responded' | Select-Object -Last 3"
} else {
    Write-Warning "Service status: $((Get-Service FollowUp).Status); health check did not pass within 60s."
    Write-Warning "Roll back: stop the service, copy $dll from $backup back into $app, start the service."
}
