[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$openApiPath = Join-Path $repoRoot 'docs/contracts/cib-v4/openapi.json'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$apiUrl = "http://127.0.0.1:$port"
$logBase = Join-Path ([System.IO.Path]::GetTempPath()) ('aipms-openapi-' + [Guid]::NewGuid().ToString('N'))
# Contract generation must never contact a shared database or dispatch queued work.
$processEnvironment = @{
    ASPNETCORE_ENVIRONMENT = 'Development'
    ConnectionStrings__DefaultConnection = 'Server=127.0.0.1,1;Database=AI_PMS_OPENAPI_OFFLINE;Integrated Security=true;Connect Timeout=1;'
    NotificationEmail__Enabled = 'false'
    ScheduledNotifications__Enabled = 'false'
    MeetingReminders__Enabled = 'false'
    VideoMeeting__Enabled = 'false'
    GoogleAuth__Enabled = 'false'
    PasswordRecovery__Enabled = 'false'
    Chat__Enabled = 'false'
    NotificationRealtime__Enabled = 'false'
    RiskAlert__Enabled = 'false'
    GoogleChallengeCleanup__Enabled = 'false'
    Logging__LogLevel__Default = 'Warning'
    Logging__LogLevel__Microsoft = 'Warning'
    Jwt__Issuer = 'AI-PMS.OpenApi'
    Jwt__Audience = 'AI-PMS.OpenApi'
    Jwt__SigningKey = 'offline-openapi-generation-only-not-a-deployment-signing-key-123456789'
    Observability__LogFilePath = $logBase + '-app.log'
}
foreach ($k in $processEnvironment.Keys) {
    [System.Environment]::SetEnvironmentVariable($k, $processEnvironment[$k], "Process")
}
$apiDll = Join-Path $repoRoot 'src/AIPMS.Api/bin/Release/net8.0/AIPMS.Api.dll'
$start = @{
    FilePath = 'dotnet'
    ArgumentList = @($apiDll, '--urls', $apiUrl)
    WorkingDirectory = $repoRoot
    PassThru = $true
    RedirectStandardOutput = $logBase + '-out.log'
    RedirectStandardError = $logBase + '-err.log'
}
if ($IsWindows) { $start.WindowStyle = 'Hidden' }
$process = Start-Process @start
try {
    $rawSwagger = $null
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 30) {
        if ($process.HasExited) { throw "OpenAPI host exited. Diagnostics: $logBase-out.log and $logBase-err.log" }
        try {
            $response = Invoke-WebRequest "$apiUrl/swagger/v1/swagger.json" -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200 -and -not [string]::IsNullOrWhiteSpace($response.Content)) {
                $rawSwagger = $response.Content
                break
            }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ([string]::IsNullOrWhiteSpace($rawSwagger)) { throw 'OpenAPI host did not respond within 30 seconds.' }

    $tempRaw = Join-Path ([System.IO.Path]::GetTempPath()) ('swagger-raw-' + [Guid]::NewGuid().ToString('N') + '.json')
    [System.IO.File]::WriteAllText($tempRaw, [string]$rawSwagger)
    try {
        $formatterDll = Join-Path $repoRoot 'scripts/FormatOpenApi/bin/Release/net8.0/FormatOpenApi.dll'
        & dotnet $formatterDll $tempRaw $openApiPath
        if ($LASTEXITCODE -ne 0) { throw "FormatOpenApi failed with exit code $LASTEXITCODE" }
    } finally {
        Remove-Item -Path $tempRaw -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'Updated OpenAPI from an offline host with delivery workers disabled.'
} finally {
    if ($process -and -not $process.HasExited) {
        if ($IsWindows) {
            & taskkill /PID $process.Id /T /F 2>$null | Out-Null
        } else {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        [void]$process.WaitForExit(5000)
    }
    if ($process) { $process.Dispose() }
}