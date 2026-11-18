[CmdletBinding()]
param([string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION,
    [ValidateRange(0,500)][int]$LargeProjectFileMiB = 0,
    [string]$OutputPath = (Join-Path ([IO.Path]::GetTempPath()) ('aipms-v5-login-' + [Guid]::NewGuid().ToString('N') + '.json')))
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Supply AIPMS_TEST_SQL_CONNECTION privately.' }
$root = Split-Path -Parent $PSScriptRoot
$name = 'AI_PMS_E2E_' + [Guid]::NewGuid().ToString('N')
$create = Join-Path $PSScriptRoot 'new-e2e-database.ps1'
$runRoot = Join-Path ([IO.Path]::GetTempPath()) ('aipms-v5-api-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $runRoot)
$previousPassword = $env:AIPMS_E2E_PASSWORD
$env:AIPMS_E2E_PASSWORD = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$process = $null
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$url = "http://127.0.0.1:$port"
try {
    & $create -ConnectionString $ConnectionString -DatabaseName $name | Out-Null
    $neutral = [Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString($ConnectionString); [void]$neutral.Remove('Command Timeout')
    $sql = [Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
    $sql['Initial Catalog'] = $name
    $settings = @{
        ASPNETCORE_ENVIRONMENT='Staging'; DOTNET_ENVIRONMENT='Staging'; ConnectionStrings__DefaultConnection=$sql.ConnectionString
        Cors__AllowedOrigins__0='http://localhost:5173'
        Jwt__Issuer='AI-PMS.IsolatedAcceptance'; Jwt__Audience='AI-PMS.IsolatedAcceptance'
        Jwt__SigningKey=([Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N'))
        FileStorage__Provider='Local'; FileStorage__RootPath=(Join-Path $runRoot 'files')
        PasswordRecovery__KeyRingPath=(Join-Path $runRoot 'keys'); PasswordRecovery__Enabled='false'
        GoogleAuth__Enabled='false'; NotificationEmail__Enabled='false'; ScheduledNotifications__Enabled='false'
        MeetingReminders__Enabled='false'; VideoMeeting__Enabled='false'; Chat__Enabled='false'; Chat__RealtimeEnabled='false'
        Observability__LogFilePath=(Join-Path $runRoot 'api-.log')
    }
    $start = @{
        FilePath='dotnet'; ArgumentList=@('bin/Release/net8.0/AIPMS.Api.dll','--urls',$url,'--contentRoot',('"'+$runRoot+'"'))
        WorkingDirectory=(Join-Path $root 'src/AIPMS.Api'); Environment=$settings; PassThru=$true
        RedirectStandardOutput=(Join-Path $runRoot 'stdout.log'); RedirectStandardError=(Join-Path $runRoot 'stderr.log')
    }
    if ($IsWindows) { $start.WindowStyle='Hidden' }
    $process = Start-Process @start
    $timer = [Diagnostics.Stopwatch]::StartNew(); $ready = $false
    while ($timer.Elapsed.TotalSeconds -lt 40) {
        if ($process.HasExited) { throw "Isolated API exited; inspect $runRoot." }
        try { [void](Invoke-WebRequest "$url/api/v1/system" -TimeoutSec 2); $ready=$true; break }
        catch { Start-Sleep -Milliseconds 400 }
    }
    if (!$ready) { throw "Isolated API unavailable; inspect $runRoot." }
    $anonymous = Invoke-WebRequest "$url/api/v1/dashboards/me" -SkipHttpErrorCheck
    if ($anonymous.StatusCode -ne 401) { throw 'Anonymous dashboard was not denied.' }
    $actors = @()
    foreach ($email in @('admin@e2e.invalid','v5.a.leader@e2e.invalid','v5.staff.it@e2e.invalid','v5.primary@e2e.invalid')) {
        $payload = @{email=$email;password=$env:AIPMS_E2E_PASSWORD} | ConvertTo-Json
        $login = Invoke-RestMethod "$url/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body $payload
        if ([string]::IsNullOrWhiteSpace($login.accessToken)) { throw 'Real login did not return an access token.' }
        $dashboard = Invoke-WebRequest "$url/api/v1/dashboards/me" -Headers @{Authorization='Bearer '+$login.accessToken}
        if ($dashboard.StatusCode -ne 200) { throw 'Authenticated dashboard failed.' }
        $actors += @{alias=$email;login=200;dashboard=200}
        $login=$null; $payload=$null
    }
    $largeUploadEvidence = $null
    if ($LargeProjectFileMiB -gt 0) {
        # All fixture mutations stay inside the owned, disposable catalog created above.
        $fixture = [Data.SqlClient.SqlConnection]::new($sql.ConnectionString)
        try {
            $fixture.Open()
            $command = $fixture.CreateCommand()
            $command.CommandText = @'
IF DB_NAME() NOT LIKE N'AI_PMS_E2E[_]%' THROW 51000, 'Owned fixture required.', 1;
DECLARE @project bigint=(SELECT entity_id FROM dbo.e2e_aliases WHERE alias_name=N'inter-project');
DECLARE @actor bigint=(SELECT id FROM dbo.users WHERE email=N'leader@e2e.invalid');
UPDATE dbo.projects SET status=N'ACTIVE' WHERE id=@project;
INSERT dbo.progress_reports(project_id,submitted_by,report_type,period_start,period_end,summary,status)
VALUES(@project,@actor,N'WEEKLY','2026-11-16','2026-11-22',N'Large upload runtime fixture',N'DRAFT');
SELECT CONVERT(bigint,SCOPE_IDENTITY());
'@
            $reportId = [long]$command.ExecuteScalar()
            $command.Dispose()
        } finally { $fixture.Dispose() }
        $login = Invoke-RestMethod "$url/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body (
            @{email='leader@e2e.invalid';password=$env:AIPMS_E2E_PASSWORD} | ConvertTo-Json)
        $http = [Net.Http.HttpClient]::new()
        $http.Timeout = [TimeSpan]::FromMinutes(5)
        $http.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $login.accessToken)
        $authHeaders = @{Authorization='Bearer '+$login.accessToken}
        try {
            $size = [long]$LargeProjectFileMiB * 1024 * 1024
            $request = @{parentType='REPORT';parentId=$reportId;fileName='runtime-large.pdf';contentType='application/pdf';sizeBytes=$size}
            $session = Invoke-RestMethod "$url/api/v1/files/upload-sessions" -Method Post -Headers $authHeaders -ContentType 'application/json' -Body ($request | ConvertTo-Json)
            $sessionUrl = "$url/api/v1/files/upload-sessions/$($session.sessionId)"
            $bytes = [byte[]]::new([int]$session.chunkSizeBytes)
            $header = [Text.Encoding]::ASCII.GetBytes("%PDF-1.7`n")
            [Array]::Copy($header,$bytes,$header.Length)
            $expected = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA256)
            try {
                for ([long]$offset=0; $offset -lt $size;) {
                    $count = [int][Math]::Min($bytes.Length,$size-$offset)
                    $body = [Net.Http.ByteArrayContent]::new($bytes,0,$count)
                    $body.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
                    try {
                        $response = $http.PutAsync("$sessionUrl/chunks?offset=$offset",$body).GetAwaiter().GetResult()
                        try {
                            if (!$response.IsSuccessStatusCode) { throw "Chunk rejected with HTTP $([int]$response.StatusCode)." }
                            $session = ($response.Content.ReadAsStringAsync().GetAwaiter().GetResult()) | ConvertFrom-Json
                        } finally { $response.Dispose() }
                    } finally { $body.Dispose() }
                    $expected.AppendData($bytes,0,$count)
                    $offset += $count
                    if ($session.nextOffset -ne $offset) { throw 'Server offset differs from acknowledged bytes.' }
                    if ($offset -eq $count) {
                        # Verify actual Kestrel body limits (TestServer does not enforce that feature).
                        $oversize = [Net.Http.ByteArrayContent]::new([byte[]]::new([int]$session.chunkSizeBytes+1))
                        $oversize.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('application/octet-stream')
                        try {
                            $denied = $http.PutAsync("$sessionUrl/chunks?offset=$offset",$oversize).GetAwaiter().GetResult()
                            try { if ([int]$denied.StatusCode -ne 413) { throw 'Oversize raw chunk was not rejected with 413.' } }
                            finally { $denied.Dispose() }
                        } finally { $oversize.Dispose() }
                        # Restart the owned local API, then continue with the same durable session and JWT.
                        Stop-Process -Id $process.Id; [void]$process.WaitForExit(5000); $process.Dispose()
                        $process = Start-Process @start
                        $restartTimer=[Diagnostics.Stopwatch]::StartNew(); $restarted=$false
                        while ($restartTimer.Elapsed.TotalSeconds -lt 40) {
                            if ($process.HasExited) { throw 'Restarted isolated API exited.' }
                            try { [void](Invoke-WebRequest "$url/api/v1/system" -TimeoutSec 2); $restarted=$true; break }
                            catch { Start-Sleep -Milliseconds 200 }
                        }
                        if (!$restarted) { throw 'Restarted isolated API did not become ready.' }
                        $readback = Invoke-RestMethod $sessionUrl -Headers $authHeaders
                        if ($readback.nextOffset -ne $offset) { throw 'Upload did not preserve its offset across API restart.' }
                    }
                    [Array]::Clear($bytes)
                }
                $hash = [Convert]::ToHexString($expected.GetHashAndReset()).ToLowerInvariant()
            } finally { $expected.Dispose() }
            $session = Invoke-RestMethod "$sessionUrl/complete" -Method Post -Headers $authHeaders
            $uploadTimer = [Diagnostics.Stopwatch]::StartNew()
            while ($session.state -eq 'PROCESSING' -and $uploadTimer.Elapsed.TotalMinutes -lt 3) {
                Start-Sleep -Milliseconds 200
                $session = Invoke-RestMethod $sessionUrl -Headers $authHeaders
            }
            if ($session.state -ne 'COMPLETED' -or $session.file.sizeBytes -ne $size -or $session.file.sha256 -ne $hash) {
                throw 'Large file did not reach confirmed completion with matching metadata.'
            }
            $download = $http.GetAsync("$url/api/v1/files/$($session.file.id)/download",[Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
            try {
                if (!$download.IsSuccessStatusCode -or $download.Content.Headers.ContentLength -ne $size) { throw 'Download headers/length mismatch.' }
                $stream = $download.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
                $sha=[Security.Cryptography.SHA256]::Create()
                try { $actualHash=[Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() }
                finally { $sha.Dispose(); $stream.Dispose() }
                if ($actualHash -ne $hash) { throw 'Downloaded file checksum mismatch.' }
            } finally { $download.Dispose() }
            $largeUploadEvidence = @{sizeBytes=$size;chunkBytes=$bytes.Length;actualHttpServer='Kestrel';passwordLogin=$true;apiRestartResumed=$true;oversizeChunkStatus=413;state='COMPLETED';downloadChecksumMatch=$true;storage='Real local disk';externalDriveVerified=$false}
        } finally { $http.Dispose(); $login=$null; $authHeaders=$null }
    }
    $evidence = @{ completedAtUtc=[DateTime]::UtcNow; sourceCommit=(& git -C $root rev-parse HEAD)
        sourceDirty=[bool](& git -C $root status --porcelain)
        authentication='Real password login and JWT middleware; synthetic identities, no Google mailbox'
        database='Owned per-run E2E catalog'; anonymousDashboard=401; actors=$actors
        externalDeliveryDisabled=$true; sharedDatabaseMutated=$false; cleanup='Owned database and API stopped in finally'
        largeProjectFile=$largeUploadEvidence
    }
} finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { Stop-Process -Id $process.Id; [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
    try { & $create -ConnectionString $ConnectionString -DatabaseName $name -Drop | Out-Null }
    finally { $env:AIPMS_E2E_PASSWORD=$previousPassword }
    $ownedFiles = [IO.Path]::GetFullPath((Join-Path $runRoot 'files'))
    $ownedRunRoot = [IO.Path]::GetFullPath($runRoot)
    if ((Split-Path -Leaf $ownedRunRoot) -cnotmatch '^aipms-v5-api-[a-f0-9]{32}$' -or
        !$ownedFiles.StartsWith($ownedRunRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean an unexpected runtime upload directory.'
    }
    if (Test-Path -LiteralPath $ownedFiles) { Remove-Item -LiteralPath $ownedFiles -Recurse -Force }
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "PASS: isolated API startup, real password/JWT login for four actors, anonymous denial and cleanup. Evidence: $OutputPath"
