# Deploy: Statistics Oracle sync (Test + Lab) — backend DLLs + frontend wwwroot.
# Run in an elevated PowerShell. Stops FollowUp, backs up, copies, restarts.
# Copies the 3 changed managed assemblies (Api/Application/Infrastructure) + mirrors wwwroot.
$ErrorActionPreference = 'Stop'
$app    = 'C:\FollowUp\app'
$srcBin = 'D:\App\src\FollowUp.Api\bin\Release\net8.0'
$srcWeb = 'D:\App\src\FollowUp.Api\wwwroot'
$dlls   = @('FollowUp.Api.dll', 'FollowUp.Application.dll', 'FollowUp.Infrastructure.dll')

$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-teststats-$stamp"
New-Item -ItemType Directory -Path $backup -Force | Out-Null
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null

Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { Copy-Item "$app\$d" $backup -Force }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Stopping FollowUp service..."
Stop-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

Write-Host "Copying new DLLs..."
foreach ($d in $dlls) { Copy-Item "$srcBin\$d" $app -Force }

Write-Host "Mirroring new wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

Start-Sleep -Seconds 3
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "If needed, roll back by copying DLLs + wwwroot from $backup and restarting."
}
