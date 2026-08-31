# Reset the FollowUp dev Postgres (port 5442) password for user "postgres".
# Opens a temporary loopback-only trust window, sets the password, restores scram-sha-256.
$ErrorActionPreference = 'Stop'
$Hba  = 'D:\FollowUpDevDb\pgdata\pg_hba.conf'
$Bin  = 'D:\followup\pgsql\pgsql\bin'
$Data = 'D:\FollowUpDevDb\pgdata'
$NewPw = 'FollowUp_Dev_2026!'      # change this if you want a different password

Write-Host '1/5 backing up pg_hba.conf...'
Copy-Item $Hba "$Hba.bak.followup" -Force

Write-Host '2/5 opening temporary loopback trust window...'
$orig = Get-Content $Hba -Raw
Set-Content $Hba ("host    all    postgres    127.0.0.1/32    trust`r`n" + $orig) -Encoding ASCII
& "$Bin\pg_ctl.exe" -D $Data reload | Out-Null

Write-Host '3/5 setting new password...'
& "$Bin\psql.exe" -h 127.0.0.1 -p 5442 -U postgres -d followup -c "ALTER USER postgres PASSWORD '$NewPw';"

Write-Host '4/5 restoring original scram-sha-256 auth...'
Copy-Item "$Hba.bak.followup" $Hba -Force
& "$Bin\pg_ctl.exe" -D $Data reload | Out-Null

Write-Host '5/5 verifying login with the new password...'
$env:PGPASSWORD = $NewPw
& "$Bin\psql.exe" -h 127.0.0.1 -p 5442 -U postgres -d followup -c '\dt' | Out-Null
Write-Host ""
Write-Host "OK - password is now: $NewPw"
