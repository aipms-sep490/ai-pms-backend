param(
    [switch]$CheckRemote
)

# Read-only diagnostics: no messages, uploads, folder creation or sharing changes.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../src/AIPMS.Api/AIPMS.Api.csproj'
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$secretId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
$secretRoot = if ($env:APPDATA) { Join-Path $env:APPDATA 'Microsoft/UserSecrets' }
    else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.microsoft/usersecrets' }
$secretPath = Join-Path $secretRoot "$secretId/secrets.json"
$script:secretValues = @{}
if (Test-Path -LiteralPath $secretPath) {
    $script:secretValues = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json -AsHashtable
}
function Setting([string]$key, [string]$fallback = '') {
    $value = [Environment]::GetEnvironmentVariable($key.Replace(':', '__'))
    if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
    if ($script:secretValues.ContainsKey($key)) { return [string]$script:secretValues[$key] }
    return $fallback
}

$driveEnabled = (Setting 'FileStorage:Provider' 'Local') -eq 'GoogleDrive'
$driveReady = $true
foreach ($key in @('GoogleDrive:ClientId', 'GoogleDrive:ClientSecret', 'GoogleDrive:RefreshToken', 'GoogleDrive:FolderId')) {
    if ([string]::IsNullOrWhiteSpace((Setting $key))) { $driveReady = $false }
}
$folder = Setting 'GoogleDrive:FolderId'
if ($folder -notmatch '^[a-zA-Z0-9_-]{1,255}$') { $driveReady = $false }
Write-Output ([pscustomobject]@{ Integration = 'GoogleDrive'; Enabled = $driveEnabled; ConfigurationComplete = $driveReady })

$emailReady = $true
foreach ($key in @('Email:Host', 'Email:SenderAddress', 'Email:Username', 'Email:Password')) {
    if ([string]::IsNullOrWhiteSpace((Setting $key))) { $emailReady = $false }
}
$smtpPort = 0
if (-not [int]::TryParse((Setting 'Email:Port' '587'), [ref]$smtpPort) -or $smtpPort -lt 1 -or $smtpPort -gt 65535) { $emailReady = $false }
if ((Setting 'Email:EnableSsl' 'true') -ne 'true') { $emailReady = $false }
Write-Output ([pscustomobject]@{ Integration = 'SMTP'; Enabled = ((Setting 'NotificationEmail:Enabled' 'false') -eq 'true'); ConfigurationComplete = $emailReady })
if (-not $CheckRemote) { return }

if ($driveReady) {
    try {
        $token = Invoke-RestMethod -Method Post -Uri 'https://oauth2.googleapis.com/token' -TimeoutSec 15 -Body @{
            client_id = (Setting 'GoogleDrive:ClientId'); client_secret = (Setting 'GoogleDrive:ClientSecret')
            refresh_token = (Setting 'GoogleDrive:RefreshToken'); grant_type = 'refresh_token'
        }
        $metadata = Invoke-RestMethod -Uri "https://www.googleapis.com/drive/v3/files/${folder}?fields=id,mimeType,trashed,capabilities(canAddChildren)" `
            -Headers @{ Authorization = "Bearer $($token.access_token)" } -TimeoutSec 15
        $writable = $metadata.mimeType -eq 'application/vnd.google-apps.folder' -and $metadata.trashed -ne $true -and $metadata.capabilities.canAddChildren -eq $true
        Write-Output ([pscustomobject]@{ Integration = 'GoogleDrive'; RemoteCheck = 'Folder metadata'; Passed = $writable })
    }
    catch { Write-Output 'GoogleDrive remote check failed; verify credential validity, folder visibility and write permission.' }
    finally { $token = $null }
}

if ($emailReady) {
    $tcp = [Net.Sockets.TcpClient]::new()
    $tls = $null
    try {
        $smtpHost = Setting 'Email:Host'
        $connect = $tcp.ConnectAsync($smtpHost, $smtpPort)
        if (-not $connect.Wait(10000)) { throw 'Connect timeout' }
        $stream = $tcp.GetStream(); $stream.ReadTimeout = 10000; $stream.WriteTimeout = 10000
        $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
        $writer = [IO.StreamWriter]::new($stream, [Text.Encoding]::ASCII, 1024, $true)
        $writer.NewLine = "`r`n"; $writer.AutoFlush = $true
        function Read-SmtpReply {
            do { $line = $reader.ReadLine(); if ($null -eq $line) { throw 'SMTP closed' } } while ($line.Length -gt 3 -and $line[3] -eq '-')
            return $line
        }
        if ((Read-SmtpReply) -notmatch '^220 ') { throw 'Unexpected greeting' }
        $writer.WriteLine('EHLO aipms-diagnostics')
        if ((Read-SmtpReply) -notmatch '^250 ') { throw 'EHLO failed' }
        $writer.WriteLine('STARTTLS')
        if ((Read-SmtpReply) -notmatch '^220 ') { throw 'STARTTLS failed' }
        $reader.Dispose(); $writer.Dispose()
        $tls = [Net.Security.SslStream]::new($stream, $false)
        $handshake = $tls.AuthenticateAsClientAsync($smtpHost)
        if (-not $handshake.Wait(10000)) { throw 'TLS timeout' }
        Write-Output 'SMTP TLS/certificate check passed. Authentication and message delivery were not attempted.'
    }
    catch { Write-Output 'SMTP transport check failed; verify host, port, network and TLS certificate.' }
    finally { if ($tls) { $tls.Dispose() }; $tcp.Dispose() }
}
