# FollowUp nightly backup - database (pg_dump) + application folder, 7-day rolling retention, latest copy on GitHub.
# -------------------------------------------------------------------------------------------------
# What one run does (02:00 daily via the "FollowUp Nightly Backup" scheduled task, running as SYSTEM):
#   1. pg_dump (custom format, compressed) of the live database, using the FOLLOWUP_DB connection string of the
#      FollowUp Windows service (read from its registry Environment) -> <Root>\<yyyy-MM-dd>\followup-db-<date>.dump
#      The archive is verified with pg_restore --list before anything else happens.
#   2. Zip of C:\FollowUp\app (DLLs, wwwroot, config; logs excluded) + an export of the service's registry key
#      (the service definition and its environment) -> <Root>\<yyyy-MM-dd>\followup-app-<date>.zip
#   3. manifest.json with sizes, SHA-256 hashes, the app file version and the database name.
#   4. Retention: after a successful run every dated folder older than <RetentionDays> (default 7) is deleted, so the
#      folder of the same weekday last week goes when today's backup is complete. Seven folders stay on disk.
#   5. Off-box copy (skip with -NoUpload): the dump and the zip are encrypted (AES-256, openssl, key file
#      <Root>\.backup-key) and uploaded as assets of the release "<ReleaseTag>" in the PRIVATE GitHub repository
#      <GitHubOwner>/<GitHubRepo> (created on first run). The previous assets are deleted first, so GitHub always
#      holds exactly the latest backup. Files above <PartSizeMB> are split into numbered parts (GitHub caps one asset at 2 GB).
#   6. Outcome -> <Root>\backup.log and the Windows Application event log (source FollowUpBackup: 1000 = OK, 1001 = failed).
#
# RESTORE (database):   pg_restore -h <host> -p <port> -U <user> -d <db> --clean --if-exists <folder>\followup-db-<date>.dump
# RESTORE (app):        stop the FollowUp service, expand followup-app-<date>.zip over C:\FollowUp\app, start the service.
#                       The zip also holds followup-service.reg (service definition incl. FOLLOWUP_* environment) for a rebuild.
# RESTORE (from GitHub): download the assets of release <ReleaseTag>; if a file came in parts, join them first:
#                         cmd /c copy /b followup-db-<date>.dump.enc.part001+followup-db-<date>.dump.enc.part002 followup-db-<date>.dump.enc
#                       then decrypt with the key file (keep a copy of <Root>\.backup-key OFF this machine - without it the
#                       GitHub copy is unreadable):
#                         openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass file:.backup-key -in followup-db-<date>.dump.enc -out followup-db-<date>.dump
#
# Secrets used, never printed: the database password (PGPASSWORD for the duration of pg_dump only) and the GitHub token
# (<Root>\.github-token, or the FOLLOWUP_BACKUP_GITHUB_TOKEN environment variable). Both files live in <Root>, whose ACL the
# installer restricts to Administrators + SYSTEM.
# -------------------------------------------------------------------------------------------------
param(
    [string]$Root = 'D:\backup',
    [int]$RetentionDays = 7,
    [switch]$NoUpload,
    [string]$GitHubOwner = 'arfaawadnt',
    [string]$GitHubRepo = 'Follow-Up-backups',
    [string]$ReleaseTag = 'backup-latest',
    [int]$PartSizeMB = 1500
)

$ErrorActionPreference = 'Stop'
$app       = 'C:\FollowUp\app'
$pgBin     = 'C:\Program Files\PostgreSQL\17\bin'
$openssl   = 'C:\Program Files\Git\usr\bin\openssl.exe'
$curl      = Join-Path $env:SystemRoot 'System32\curl.exe'
$eventSrc  = 'FollowUpBackup'
$today     = Get-Date
$stamp     = $today.ToString('yyyy-MM-dd')
$dir       = Join-Path $Root $stamp
$logFile   = Join-Path $Root 'backup.log'
$keyFile   = Join-Path $Root '.backup-key'
$tokenFile = Join-Path $Root '.github-token'
$started   = Get-Date

New-Item -ItemType Directory -Force -Path $Root | Out-Null

function Log([string]$msg) {
    $line = '{0:yyyy-MM-dd HH:mm:ss} {1}' -f (Get-Date), $msg
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}
function WriteEvent([string]$type, [int]$id, [string]$msg) {
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists($eventSrc)) { New-EventLog -LogName Application -Source $eventSrc }
        Write-EventLog -LogName Application -Source $eventSrc -EntryType $type -EventId $id -Message $msg
    } catch { }
}
function Fail([string]$msg) {
    Log "ERROR: $msg"
    WriteEvent 'Error' 1001 "FollowUp nightly backup FAILED: $msg (see $logFile)"
    exit 1
}
function Sha256([string]$path) { (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant() }
function Mb([long]$bytes) { [math]::Round($bytes / 1MB, 1) }

# ---- 0. Connection string of the live service (never printed) -------------------------------------------------------
function Get-ServiceDb {
    $svcEnv = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Services\FollowUp' -Name Environment).Environment
    $cs = ($svcEnv | Where-Object { $_ -like 'FOLLOWUP_DB=*' } | Select-Object -First 1)
    if (-not $cs) { throw 'FOLLOWUP_DB not found in the FollowUp service environment.' }
    $cs = $cs.Substring('FOLLOWUP_DB='.Length)
    $kv = @{}
    foreach ($part in ($cs -split ';')) { if ($part -match '^\s*([^=]+?)\s*=\s*(.*?)\s*$') { $kv[$matches[1].ToLowerInvariant()] = $matches[2] } }
    $h = if ($kv['host']) { $kv['host'] } elseif ($kv['server']) { $kv['server'] } else { 'localhost' }
    $p = if ($kv['port']) { $kv['port'] } else { '5432' }
    $u = if ($kv['username']) { $kv['username'] } elseif ($kv['user id']) { $kv['user id'] } else { $kv['user'] }
    if (-not $kv['database'] -or -not $u) { throw 'Could not parse Database/Username from FOLLOWUP_DB.' }
    return @{ Host = $h; Port = $p; Db = $kv['database']; User = $u; Password = $kv['password'] }
}

try {
    Log "=== Backup $stamp started (root $Root, retention $RetentionDays days, upload $(-not $NoUpload))"
    if (-not (Test-Path "$pgBin\pg_dump.exe")) { throw "pg_dump not found at $pgBin" }
    if (-not (Test-Path $app)) { throw "Application folder $app not found" }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null

    # ---- 1. Database --------------------------------------------------------------------------------------------------
    $db = Get-ServiceDb
    $dumpFile = Join-Path $dir "followup-db-$stamp.dump"
    Log "pg_dump of '$($db.Db)' on $($db.Host):$($db.Port) -> $dumpFile"
    $env:PGPASSWORD = $db.Password
    try {
        & "$pgBin\pg_dump.exe" -h $db.Host -p $db.Port -U $db.User -d $db.Db -Fc -f $dumpFile
        if ($LASTEXITCODE -ne 0) { throw "pg_dump failed (exit $LASTEXITCODE)" }
    } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
    $toc = & "$pgBin\pg_restore.exe" --list $dumpFile 2>&1
    if ($LASTEXITCODE -ne 0) { throw "pg_restore --list could not read the dump (exit $LASTEXITCODE): $($toc | Select-Object -Last 1)" }
    $tocEntries = @($toc | Where-Object { $_ -match '^\d+;' }).Count
    if ($tocEntries -lt 50) { throw "Dump looks incomplete: only $tocEntries catalogue entries" }
    Log "Database OK: $(Mb (Get-Item $dumpFile).Length) MB, $tocEntries catalogue entries"

    # ---- 2. Application -----------------------------------------------------------------------------------------------
    $zipFile = Join-Path $dir "followup-app-$stamp.zip"
    $staging = Join-Path $env:TEMP "followup-backup-$stamp"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    $stagedApp = Join-Path $staging 'app'
    New-Item -ItemType Directory -Force -Path $stagedApp | Out-Null
    # robocopy reads the DLLs the running service has loaded (Compress-Archive cannot open an in-use file directly).
    & robocopy.exe $app $stagedApp /MIR /XD logs /R:2 /W:2 /NFL /NDL /NP /NJH /NJS | Out-Null
    if ($LASTEXITCODE -gt 3) { throw "robocopy of $app failed (exit $LASTEXITCODE)" }
    & reg.exe export 'HKLM\SYSTEM\CurrentControlSet\Services\FollowUp' (Join-Path $stagedApp 'followup-service.reg') /y | Out-Null
    if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
    Compress-Archive -Path (Join-Path $stagedApp '*') -DestinationPath $zipFile -CompressionLevel Optimal
    Remove-Item $staging -Recurse -Force
    $apiDll = Join-Path $app 'FollowUp.Api.dll'
    $appVersion = if (Test-Path $apiDll) { (Get-Item $apiDll).VersionInfo.FileVersion } else { 'unknown' }
    Log "Application OK: $(Mb (Get-Item $zipFile).Length) MB (FollowUp.Api.dll $appVersion, service key included, logs excluded)"

    # ---- 3. Manifest ----------------------------------------------------------------------------------------------------
    $manifest = [ordered]@{
        date = $stamp; createdAt = (Get-Date).ToString('o'); machine = $env:COMPUTERNAME
        database = [ordered]@{ name = $db.Db; host = $db.Host; port = $db.Port; file = (Split-Path $dumpFile -Leaf); bytes = (Get-Item $dumpFile).Length; sha256 = (Sha256 $dumpFile); catalogueEntries = $tocEntries }
        application = [ordered]@{ folder = $app; version = $appVersion; file = (Split-Path $zipFile -Leaf); bytes = (Get-Item $zipFile).Length; sha256 = (Sha256 $zipFile) }
        retentionDays = $RetentionDays
        restore = @('pg_restore -h HOST -p PORT -U USER -d DB --clean --if-exists followup-db-DATE.dump',
                    'stop service FollowUp; expand followup-app-DATE.zip over C:\FollowUp\app; start service')
    }
    $manifestFile = Join-Path $dir 'manifest.json'
    ($manifest | ConvertTo-Json -Depth 4) | Set-Content -Path $manifestFile -Encoding UTF8
    Log "Manifest written"

    # ---- 4. Retention: drop dated folders older than the window (the same weekday last week goes today) -------------
    $cutoff = $today.Date.AddDays(-$RetentionDays)
    foreach ($d in Get-ChildItem $Root -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}$' }) {
        $when = [datetime]::ParseExact($d.Name, 'yyyy-MM-dd', $null)
        if ($when -le $cutoff) { Remove-Item $d.FullName -Recurse -Force; Log "Retention: removed $($d.Name)" }
    }
    $kept = @(Get-ChildItem $Root -Directory | Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}$' }).Count
    Log "Retention: $kept dated backup folder(s) on disk"

    # ---- 5. Off-box copy: encrypted release assets in the private GitHub repository -----------------------------------
    $uploaded = 'skipped'
    if (-not $NoUpload) {
        $token = $env:FOLLOWUP_BACKUP_GITHUB_TOKEN
        if (-not $token -and (Test-Path $tokenFile)) { $token = (Get-Content $tokenFile -Raw).Trim() }
        if (-not $token) { throw "No GitHub token: put one in $tokenFile (the installer does this) or set FOLLOWUP_BACKUP_GITHUB_TOKEN" }
        if (-not (Test-Path $openssl)) { throw "openssl not found at $openssl (Git for Windows)" }
        if (-not (Test-Path $keyFile)) {
            $key = (& $openssl rand -base64 48); if ($LASTEXITCODE -ne 0) { throw 'openssl rand failed' }
            Set-Content -Path $keyFile -Value $key -Encoding ASCII -NoNewline
            Log "Encryption key generated at $keyFile - COPY IT TO A SAFE PLACE OFF THIS MACHINE (the GitHub copy is unreadable without it)"
            WriteEvent 'Warning' 1002 "A new backup encryption key was generated at $keyFile. Copy it off this machine; the GitHub backup copy cannot be decrypted without it."
        }
        $hdr = @('-H', "Authorization: Bearer $token", '-H', 'Accept: application/vnd.github+json', '-H', 'User-Agent: followup-backup', '-sS', '--retry', '3', '--retry-delay', '10')
        $api = "https://api.github.com/repos/$GitHubOwner/$GitHubRepo"
        function Gh([string]$method, [string]$url, [string]$body) {
            $cargs = @('-X', $method) + $hdr + @('-w', '\n%{http_code}', $url)
            # A JSON body goes through a temp file: PowerShell 5.1 strips the double quotes when it hands a JSON string to a native exe.
            $bodyFile = $null
            if ($body) { $bodyFile = Join-Path $env:TEMP ("followup-gh-" + [guid]::NewGuid().ToString('N') + '.json'); [System.IO.File]::WriteAllText($bodyFile, $body, (New-Object System.Text.UTF8Encoding($false))); $cargs += @('-H', 'Content-Type: application/json', '--data-binary', "@$bodyFile") }
            try { $out = & $curl @cargs } finally { if ($bodyFile) { Remove-Item $bodyFile -Force -ErrorAction SilentlyContinue } }
            $lines = @($out -split "`n"); $code = [int]$lines[-1]; $json = ($lines[0..($lines.Length - 2)] -join "`n")
            return @{ Code = $code; Json = $json; Obj = $(if ($json.Trim()) { try { $json | ConvertFrom-Json } catch { $null } } else { $null }) }
        }
        # 5a. The private repository (created on first run, initialised with a README so a release can be tagged).
        $r = Gh 'GET' $api $null
        if ($r.Code -eq 404) {
            $r = Gh 'POST' 'https://api.github.com/user/repos' (@{ name = $GitHubRepo; private = $true; description = 'FollowUp nightly backups - encrypted database dump + application zip as release assets (latest only)'; has_issues = $false; has_wiki = $false; has_projects = $false; auto_init = $true } | ConvertTo-Json -Compress)
            if ($r.Code -ne 201) { throw "Could not create repository ${GitHubOwner}/${GitHubRepo}: HTTP $($r.Code) $($r.Json)" }
            Log "Created private repository $GitHubOwner/$GitHubRepo"
            Start-Sleep -Seconds 5
        } elseif ($r.Code -ne 200) { throw "GitHub repository check failed: HTTP $($r.Code) $($r.Json)" }
        elseif (-not $r.Obj.private) { throw "Repository $GitHubOwner/$GitHubRepo is NOT private - refusing to upload a database backup to it" }
        # 5b. The release that always holds the latest backup.
        $r = Gh 'GET' "$api/releases/tags/$ReleaseTag" $null
        if ($r.Code -eq 404) {
            $r = Gh 'POST' "$api/releases" (@{ tag_name = $ReleaseTag; name = "Backup $stamp"; body = 'FollowUp nightly backup (latest).'; draft = $false; prerelease = $false } | ConvertTo-Json -Compress)
            if ($r.Code -ne 201) { throw "Could not create release ${ReleaseTag}: HTTP $($r.Code) $($r.Json)" }
            Log "Created release $ReleaseTag"
        } elseif ($r.Code -ne 200) { throw "GitHub release check failed: HTTP $($r.Code) $($r.Json)" }
        $release = $r.Obj
        foreach ($a in @($release.assets)) {
            $d = Gh 'DELETE' "$api/releases/assets/$($a.id)" $null
            if ($d.Code -ne 204) { throw "Could not delete old asset $($a.name): HTTP $($d.Code)" }
            Log "GitHub: removed previous asset $($a.name)"
        }
        # 5c. Encrypt, split if needed, upload.
        $work = Join-Path $env:TEMP "followup-backup-upload-$stamp"
        if (Test-Path $work) { Remove-Item $work -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $work | Out-Null
        $assets = @()
        foreach ($src in @($dumpFile, $zipFile)) {
            $enc = Join-Path $work ((Split-Path $src -Leaf) + '.enc')
            & $openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass "file:$keyFile" -in $src -out $enc
            if ($LASTEXITCODE -ne 0) { throw "openssl encryption failed for $src" }
            $len = (Get-Item $enc).Length
            if ($len -gt ($PartSizeMB * 1MB)) {
                $in = [System.IO.File]::OpenRead($enc); $buf = New-Object byte[] (8MB); $n = 0
                try {
                    while ($in.Position -lt $in.Length) {
                        $n++; $part = "{0}.part{1:D3}" -f $enc, $n; $out = [System.IO.File]::Create($part); $written = 0L
                        try { while ($written -lt ($PartSizeMB * 1MB)) { $read = $in.Read($buf, 0, [Math]::Min($buf.Length, ($PartSizeMB * 1MB) - $written)); if ($read -le 0) { break }; $out.Write($buf, 0, $read); $written += $read } }
                        finally { $out.Dispose() }
                        $assets += $part
                    }
                } finally { $in.Dispose() }
                Remove-Item $enc -Force
                Log "Split $(Split-Path $enc -Leaf) into $n part(s) of <= $PartSizeMB MB"
            } else { $assets += $enc }
        }
        Copy-Item $manifestFile (Join-Path $work 'manifest.json'); $assets += (Join-Path $work 'manifest.json')
        foreach ($asset in $assets) {
            $name = Split-Path $asset -Leaf
            $url = "https://uploads.github.com/repos/$GitHubOwner/$GitHubRepo/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($name))"
            $out = & $curl -X POST @hdr -H 'Content-Type: application/octet-stream' --data-binary "@$asset" --max-time 14400 -w '\n%{http_code}' $url
            $code = [int](@($out -split "`n")[-1])
            if ($code -ne 201) { throw "Upload of $name failed: HTTP $code" }
            Log "GitHub: uploaded $name ($(Mb (Get-Item $asset).Length) MB)"
        }
        Remove-Item $work -Recurse -Force
        $body = "FollowUp nightly backup of $stamp (encrypted with the key at $keyFile on $env:COMPUTERNAME).`n`n" +
                "Database: $($manifest.database.file) - $(Mb $manifest.database.bytes) MB - sha256 $($manifest.database.sha256)`n" +
                "Application: $($manifest.application.file) - $(Mb $manifest.application.bytes) MB - FollowUp.Api.dll $appVersion - sha256 $($manifest.application.sha256)`n`n" +
                "Restore: join .partNNN files with copy /b, then openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass file:.backup-key -in FILE.enc -out FILE; " +
                "pg_restore --clean --if-exists the dump; expand the zip over C:\FollowUp\app (service stopped)."
        $r = Gh 'PATCH' "$api/releases/$($release.id)" (@{ name = "Backup $stamp"; body = $body } | ConvertTo-Json -Compress)
        if ($r.Code -ne 200) { Log "WARN: release note update returned HTTP $($r.Code)" }
        $uploaded = "https://github.com/$GitHubOwner/$GitHubRepo/releases/tag/$ReleaseTag"
        Log "GitHub: latest backup is $uploaded"
    }

    $elapsed = [math]::Round(((Get-Date) - $started).TotalMinutes, 1)
    $summary = "FollowUp nightly backup $stamp OK in $elapsed min: db $(Mb $manifest.database.bytes) MB, app $(Mb $manifest.application.bytes) MB, $kept folder(s) kept under $Root, GitHub copy $uploaded"
    Log $summary
    WriteEvent 'Information' 1000 $summary
    exit 0
} catch {
    Fail $_.Exception.Message
}
