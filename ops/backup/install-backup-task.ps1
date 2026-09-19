# Installs the FollowUp nightly backup: copies backup-followup.ps1 to <Root>, restricts <Root> to Administrators + SYSTEM,
# stores the GitHub token, registers the Windows event-log source and creates the scheduled task
# "FollowUp Nightly Backup" (daily 02:00, runs as SYSTEM, highest privileges, catches up a missed run at next boot).
# Re-running is safe (everything is idempotent). Run in an ELEVATED PowerShell from the repository folder:
#   .\ops\backup\install-backup-task.ps1                 # token taken from the stored git credential for github.com
#   .\ops\backup\install-backup-task.ps1 -Token ghp_...  # or a dedicated token (repo scope) for the backups repository
#   .\ops\backup\install-backup-task.ps1 -RunNow          # also start the first backup immediately
param(
    [string]$Root = 'D:\backup',
    [string]$Token,
    [string]$At = '02:00',
    [switch]$RunNow
)
$ErrorActionPreference = 'Stop'
$taskName = 'FollowUp Nightly Backup'
$source = Join-Path $PSScriptRoot 'backup-followup.ps1'
if (-not (Test-Path $source)) { throw "backup-followup.ps1 not found next to this installer ($source)" }

New-Item -ItemType Directory -Force -Path $Root | Out-Null
# Only administrators and the system account may read the folder (dumps, key, token).
& icacls.exe $Root /inheritance:r /grant:r 'BUILTIN\Administrators:(OI)(CI)F' 'NT AUTHORITY\SYSTEM:(OI)(CI)F' | Out-Null
Copy-Item $source (Join-Path $Root 'backup-followup.ps1') -Force
Write-Host "Script installed at $Root\backup-followup.ps1 (folder restricted to Administrators + SYSTEM)"

# GitHub token: explicit parameter, else the credential git already stores for github.com on this box.
$tokenFile = Join-Path $Root '.github-token'
if (-not $Token) {
    # git credential fill reads its request from stdin; feed it through cmd's input redirection (PowerShell 5.1 pipes
    # to native stdin unreliably and a 2> redirect on a native command trips ErrorActionPreference=Stop).
    $req = Join-Path $env:TEMP 'followup-cred-req.txt'
    [System.IO.File]::WriteAllText($req, "protocol=https`nhost=github.com`n`n")
    try {
        $fill = & cmd.exe /c "git credential fill < `"$req`" 2>nul"
        $line = @($fill | Where-Object { $_ -like 'password=*' } | Select-Object -First 1)
        if ($line) { $Token = $line[0].Substring('password='.Length) }
    } finally { Remove-Item $req -Force -ErrorAction SilentlyContinue }
}
if ($Token) {
    Set-Content -Path $tokenFile -Value $Token -Encoding ASCII -NoNewline
    Write-Host "GitHub token stored at $tokenFile"
} elseif (Test-Path $tokenFile) {
    Write-Host "Keeping the existing token at $tokenFile"
} else {
    Write-Warning "No GitHub token available: the nightly run will back up locally and FAIL the upload step until $tokenFile exists (re-run with -Token)."
}

if (-not [System.Diagnostics.EventLog]::SourceExists('FollowUpBackup')) { New-EventLog -LogName Application -Source 'FollowUpBackup' }

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$Root\backup-followup.ps1`" -Root `"$Root`""
$trigger   = New-ScheduledTaskTrigger -Daily -At $At
$principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 8) -RestartCount 2 -RestartInterval (New-TimeSpan -Minutes 30) -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Daily 02:00 backup of the FollowUp database (pg_dump) and application folder to D:\backup, 7-day retention, latest copy as encrypted release assets in the private GitHub repository Follow-Up-backups.' -Force | Out-Null
$t = Get-ScheduledTask -TaskName $taskName
Write-Host "Scheduled task '$taskName' registered: daily at $At as SYSTEM (state $($t.State)); next run $((Get-ScheduledTaskInfo -TaskName $taskName).NextRunTime)"

if ($RunNow) {
    Start-ScheduledTask -TaskName $taskName
    Write-Host "First backup started now; follow it in $Root\backup.log"
}
