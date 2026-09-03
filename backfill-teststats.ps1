# One-shot Test-Statistics backfill: re-syncs every month from -Start to today via the app's
# Oracle sync endpoint, so the per-(code,type) fix + visible-only filter are applied across all history.
#
# RUN THIS ONLY AFTER deploy-teststat-bytype.ps1 has shipped the visible=1 change and the service is up
# (otherwise you re-sync with the old query). Each month is idempotent (the sync delete-window replaces
# the month wholesale), so the script is safe to re-run and safe to resume after an interruption.
#
# Usage:  powershell -ExecutionPolicy Bypass -File D:\App\backfill-teststats.ps1
#         (optional: -Start 2021-01-01  -BaseUrl http://localhost:5088  -Password '...')
param(
    [datetime]$Start   = [datetime]'2021-01-01',
    [string]  $BaseUrl = 'http://localhost:5088',
    [string]  $Password
)
$ErrorActionPreference = 'Stop'

# Admin password: use -Password, else read it from the FollowUp service's registry environment.
if (-not $Password) {
    $envm = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\FollowUp' -ErrorAction SilentlyContinue).Environment
    $line = $envm | Where-Object { $_ -like 'FOLLOWUP_ADMIN_PASSWORD=*' } | Select-Object -First 1
    if ($line) { $Password = $line.Substring('FOLLOWUP_ADMIN_PASSWORD='.Length) }
}
if (-not $Password) { throw "Admin password not found. Pass -Password '<admin pwd>'." }

function Get-Token {
    $body = @{ username = 'admin'; password = $Password } | ConvertTo-Json
    $r = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/auth/login" -ContentType 'application/json' -Body $body -TimeoutSec 60
    if (-not $r.token) { throw 'Login succeeded but no token in response.' }
    return $r.token
}

function Sync-Month($from, $to, $headers) {
    $body = @{ from = $from; to = $to } | ConvertTo-Json
    return Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/test-statistics/sync" -ContentType 'application/json' -Body $body -Headers $headers -TimeoutSec 600
}

Write-Host "Logging in to $BaseUrl ..."
$script:token = Get-Token
$headers = @{ Authorization = "Bearer $script:token" }

$today  = (Get-Date).Date
$cursor = [datetime]::new($Start.Year, $Start.Month, 1)
$grand  = 0
$fail   = @()

Write-Host ("Backfilling {0:yyyy-MM-dd} to {1:yyyy-MM-dd}, one month per request..." -f $Start, $today)
Write-Host ""

while ($cursor -le $today) {
    $monthEnd = $cursor.AddMonths(1).AddDays(-1)
    if ($monthEnd -gt $today) { $monthEnd = $today }
    $from = $cursor.ToString('yyyy-MM-dd')
    $to   = $monthEnd.ToString('yyyy-MM-dd')

    $done = $false
    $reauthed = $false
    while (-not $done) {
        try {
            $res = Sync-Month $from $to $headers
            $n = [int]$res.statsUpserted
            $grand += $n
            Write-Host ("  {0} .. {1}  ->  {2,7} rows" -f $from, $to, $n)
            $done = $true
        }
        catch {
            $code = 0
            if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
            if ($code -eq 401 -and -not $reauthed) {
                Write-Host "  token expired - re-authenticating..."
                $script:token = Get-Token
                $headers = @{ Authorization = "Bearer $script:token" }
                $reauthed = $true
            }
            else {
                Write-Warning ("  {0} .. {1}  FAILED: {2}" -f $from, $to, $_.Exception.Message)
                $fail += ("{0}..{1}" -f $from, $to)
                $done = $true
            }
        }
    }
    $cursor = $cursor.AddMonths(1)
}

Write-Host ""
Write-Host ("Done. Total rows upserted: {0}." -f $grand)
if ($fail.Count -gt 0) {
    Write-Warning ("{0} month(s) failed - re-run to retry (idempotent): {1}" -f $fail.Count, ($fail -join ', '))
}
else {
    Write-Host "All months synced successfully."
}
