[CmdletBinding()]
param([string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION,
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
    $evidence = @{ completedAtUtc=[DateTime]::UtcNow; sourceCommit=(& git -C $root rev-parse HEAD)
        sourceDirty=[bool](& git -C $root status --porcelain)
        authentication='Real password login and JWT middleware; synthetic identities, no Google mailbox'
        database='Owned per-run E2E catalog'; anonymousDashboard=401; actors=$actors
        externalDeliveryDisabled=$true; sharedDatabaseMutated=$false; cleanup='Owned database and API stopped in finally'
    }
} finally {
    if ($null -ne $process) {
        if (!$process.HasExited) { Stop-Process -Id $process.Id; [void]$process.WaitForExit(5000) }
        $process.Dispose()
    }
    try { & $create -ConnectionString $ConnectionString -DatabaseName $name -Drop | Out-Null }
    finally { $env:AIPMS_E2E_PASSWORD=$previousPassword }
}
$evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Output "PASS: isolated API startup, real password/JWT login for four actors, anonymous denial and cleanup. Evidence: $OutputPath"
