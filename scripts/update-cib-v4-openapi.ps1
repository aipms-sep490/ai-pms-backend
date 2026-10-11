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
    Jwt__Issuer = 'AI-PMS.OpenApi'
    Jwt__Audience = 'AI-PMS.OpenApi'
    Jwt__SigningKey = 'offline-openapi-generation-only-not-a-deployment-signing-key-123456789'
    Observability__LogFilePath = $logBase + '-app.log'
}
$start = @{
    FilePath = 'dotnet'
    ArgumentList = @('run','--project','src/AIPMS.Api/AIPMS.Api.csproj','-c','Release','--no-build','--no-launch-profile','--urls',$apiUrl)
    WorkingDirectory = $repoRoot
    Environment = $processEnvironment
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
        try { $rawSwagger = (Invoke-WebRequest "$apiUrl/swagger/v1/swagger.json" -TimeoutSec 2).Content; break }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if ([string]::IsNullOrWhiteSpace($rawSwagger)) { throw 'OpenAPI host did not respond within 30 seconds.' }
    $doc = [System.Text.Json.JsonDocument]::Parse([string]$rawSwagger)
    try {
        $options = [System.Text.Json.JsonSerializerOptions]::new()
        $options.WriteIndented = $true
        $formatted = [System.Text.Json.JsonSerializer]::Serialize[System.Text.Json.JsonElement]($doc.RootElement, $options)
        [System.IO.File]::WriteAllText($openApiPath, $formatted)
    } finally { $doc.Dispose() }
    Write-Output 'Updated OpenAPI from an offline host with delivery workers disabled.'
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id; [void]$process.WaitForExit(5000) }
    $process.Dispose()
}