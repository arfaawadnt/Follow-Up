# Deploy: Test-statistics per-(code,type) fix + exclude non-visible tests (gt.visible=1).
# Migration 20260902225124_TestStatisticByType (test_type + unique index) is ALREADY applied in prod
# (deployed 2026-09-03 00:56). This deploy adds NO new migration — MigrateAsync finds nothing pending.
# The only net change vs running prod is the `AND gt.visible = 1` filter in OracleDefaultQueries.TestStats
# (Infrastructure.dll), re-provisioned into the Oracle config at startup. Ships all 4 DLLs + wwwroot for
# consistency (Domain/Application/Api are identical to prod; only Infrastructure differs).
# ** After deploy, RE-SYNC the history range on the Test Statistics page to (a) split collided codes into
#    per-type rows and (b) drop the ~440k legacy type-0 merged rows (which currently render as "—"). **
# Run in an ELEVATED PowerShell (the service stop/start needs admin).
$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$srcBin = 'D:\App\src\FollowUp.Api\bin\Release\net8.0'
$srcWeb = 'D:\App\src\FollowUp.Api\wwwroot'
$dlls   = @('FollowUp.Domain.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll', 'FollowUp.Api.dll')

$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-teststat-bytype-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null

Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Stopping FollowUp service..."
Stop-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

Write-Host "Copying new DLLs..."
foreach ($d in $dlls) { Copy-Item "$srcBin\$d" $app -Force }

Write-Host "Mirroring new wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service (applies migration TestStatisticByType on startup)..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

Start-Sleep -Seconds 4
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "Roll back by copying DLLs + wwwroot from $backup and restarting."
}
