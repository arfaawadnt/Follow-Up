# Deploy: Excel attachments on daily statistics emails + grouped/colour-coded Area sheet + Reference-Month filter.
# -------------------------------------------------------------------------------------------------
# NO new migration and NO Oracle feed. Ships the managed DLLs + the Angular bundle:
#   Application   : Common/Abstractions/Gateways.cs -> IEmailSender attachments overload + EmailAttachment
#   Infrastructure: Emailing/XlsxWriter.cs (dependency-free .xlsx writer, 1:1 port of the browser export
#                     util so colours/fonts match), Emailing/EmailReportsInfrastructure.cs (per-report
#                     attachments; Area sheet grouped by governorate->area and colour-coded vs a reference
#                     month), Gateways/EmailWhatsAppGateways.cs (SmtpEmailSender attaches the files)
#   wwwroot       : Email Reports editor gains a Reference Month filter (drives the Area colour coding)
#
# Domain/Api DLLs are unchanged but shipped too (same 4-DLL set as the other deploy scripts; harmless).
#
# Run in an ELEVATED PowerShell (service stop/start needs admin). By default assumes the Release DLLs and
# the Angular bundle are already built this session (dotnet build + npm run build + CSP index.html fix all
# done); pass -Build to build them here.
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

if ($Build) {
    Write-Host "Building Release DLLs..."
    & $dotnet build "$repo\src\FollowUp.Api\FollowUp.Api.csproj" -c Release --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)." }

    Write-Host "Building Angular bundle (outputs to $srcWeb)..."
    $env:Path = "$nodeDir;" + $env:Path
    Push-Location "$repo\web"
    try { & "$nodeDir\npm.cmd" run build; if ($LASTEXITCODE -ne 0) { throw "ng build failed (exit $LASTEXITCODE)." } }
    finally { Pop-Location }

    # CSP fix: strip media="print" onload="this.media='all'" (the app CSP blocks the inline onload).
    $index = "$srcWeb\index.html"
    if (Test-Path $index) {
        $html = Get-Content $index -Raw
        $fixed = $html -replace '\s*media="print"', '' -replace '\s*onload="this\.media=''all''"', ''
        if ($fixed -ne $html) {
            [System.IO.File]::WriteAllText($index, $fixed, (New-Object System.Text.UTF8Encoding($false)))
            Write-Host "Applied CSP stylesheet fix to index.html."
        }
    }
}

foreach ($d in $dlls) {
    if (-not (Test-Path "$srcBin\$d")) { throw "Missing $srcBin\$d - build first (pass -Build)." }
}
if (-not (Test-Path "$srcWeb\index.html")) { throw "Missing $srcWeb\index.html - build the Angular bundle first (pass -Build)." }

# --- Backup current DLLs + wwwroot --------------------------------------------------------------
$stamp  = Get-Date -Format 'yyyyMMdd-HHmmss'
$backup = "C:\FollowUp\app-backup-email-xlsx-$stamp"
New-Item -ItemType Directory -Path "$backup\wwwroot" -Force | Out-Null
Write-Host "Backing up current DLLs + wwwroot -> $backup"
foreach ($d in $dlls) { if (Test-Path "$app\$d") { Copy-Item "$app\$d" $backup -Force } }
robocopy "$app\wwwroot" "$backup\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

# --- Stop -> copy -> start ----------------------------------------------------------------------
Write-Host "Stopping FollowUp service..."
Stop-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

Write-Host "Copying DLLs..."
foreach ($d in $dlls) { Copy-Item "$srcBin\$d" $app -Force }
Write-Host "Mirroring wwwroot..."
robocopy $srcWeb "$app\wwwroot" /MIR /R:2 /W:2 /NFL /NDL /NP | Out-Null

Write-Host "Starting FollowUp service..."
Start-Service FollowUp
(Get-Service FollowUp).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

# --- Health check -------------------------------------------------------------------------------
Start-Sleep -Seconds 4
try {
    $r = Invoke-WebRequest -Uri 'http://localhost:5088/' -UseBasicParsing -TimeoutSec 15
    Write-Host "Service is up (HTTP $($r.StatusCode)). Backup at $backup"
    Write-Host "Verify: Email Reports -> a subscription incl. Area Statistics -> set a Reference Month -> 'Send now',"
    Write-Host "        then open the Area-Statistics-*.xlsx attachment and confirm the governorate bands + green/red day cells."
} catch {
    Write-Warning "Service started but health check failed: $($_.Exception.Message)"
    Write-Warning "Roll back by copying DLLs + wwwroot from $backup back into $app and restarting the service."
}
