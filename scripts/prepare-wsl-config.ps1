[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu-24.04',
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'

# Copy an explicit allowlist; never print secret values or pass them as process arguments.
$secretPath = Join-Path $env:APPDATA 'Microsoft/UserSecrets/AIPMS.Api-6d5e8dd0-5aa2-4c16-84f9-978c52902497/secrets.json'
$secrets = Get-Content -LiteralPath $secretPath -Raw | ConvertFrom-Json
function RequiredSecret([string]$name) {
    $value = $secrets.PSObject.Properties[$name].Value
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing User Secret: $name" }
    return [string]$value
}
$sql = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
try {
    $neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString((RequiredSecret 'ConnectionStrings:DefaultConnection'))
    [void]$neutral.Remove('Command Timeout')
    $sql.set_ConnectionString($neutral.get_ConnectionString())
} catch { throw 'Cannot parse the saved SQL connection string.' }
if ($sql.get_InitialCatalog() -ne 'AI_PMS') { throw 'Expected the existing AI_PMS database.' }
if ($sql.get_IntegratedSecurity()) { throw 'The Linux container requires SQL authentication.' }
$sql.set_DataSource('mssql,1433')

$proxyIp = (& wsl.exe -d $Distro -- docker inspect goexe-tunnel --format '{{with index .NetworkSettings.Networks "go-exe_goexe-net"}}{{.IPAddress}}{{end}}').Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the existing tunnel.' }
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($proxyIp, [ref]$parsedIp)) { throw 'Tunnel has no valid IP on go-exe_goexe-net.' }
$uid = (& wsl.exe -d $Distro -- id -u).Trim()
if ($LASTEXITCODE -ne 0 -or $uid -ne '1000') { throw 'This deployment expects the existing WSL user with UID 1000.' }

$settings = [ordered]@{
    ConnectionStrings = @{ DefaultConnection = $sql.get_ConnectionString() }
    Jwt = @{
        Issuer = RequiredSecret 'Jwt:Issuer'
        Audience = RequiredSecret 'Jwt:Audience'
        SigningKey = RequiredSecret 'Jwt:SigningKey'
        AccessTokenMinutes = 60
    }
    Cors = @{ AllowedOrigins = @('http://localhost:5173') }
    AllowedHosts = 'api-staging.khaidz.com;localhost;127.0.0.1;aipms-api'
    ReverseProxy = @{
        Enabled = $true
        KnownProxies = @($proxyIp)
        ForwardedForHeaderName = 'CF-Connecting-IP'
    }
    FileStorage = @{ Provider = 'Local'; RootPath = '/var/lib/aipms/files' }
    GoogleAuth = @{ Enabled = $false }
    PasswordRecovery = @{ Enabled = $false; KeyRingPath = '/var/lib/aipms/keys' }
    NotificationEmail = @{ Enabled = $false }
    ScheduledNotifications = @{ Enabled = $false }
    VideoMeeting = @{
        Enabled = $true
        Provider = 'LIVEKIT'
        ServerUrl = RequiredSecret 'VideoMeeting:ServerUrl'
        ApiKey = RequiredSecret 'VideoMeeting:ApiKey'
        ApiSecret = RequiredSecret 'VideoMeeting:ApiSecret'
        JoinTokenTtlSeconds = 300
        JoinOpenBeforeMinutes = 0
        JoinCloseAfterMinutes = 0
    }
    Observability = @{ LogFilePath = '/var/log/aipms/aipms-.log' }
}
$writer = @'
import json, os, pathlib, sys
config = json.load(sys.stdin)
folder = pathlib.Path.home() / ".config" / "aipms"
folder.mkdir(mode=0o700, parents=True, exist_ok=True)
os.chmod(folder, 0o700)
target = folder / "appsettings.Staging.json"
replace = sys.argv[1] == "replace"
if target.exists() and not replace:
    raise SystemExit("Config already exists. Review it or use -ReplaceExisting explicitly.")
temp = folder / (".config-" + str(os.getpid()))
try:
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        json.dump(config, stream, indent=2)
        stream.write("\n")
    os.replace(temp, target)
finally:
    if temp.exists():
        temp.unlink()
print("Private config ready: " + str(target))
'@
$mode = if ($ReplaceExisting) { 'replace' } else { 'create' }
$settings | ConvertTo-Json -Depth 6 | & wsl.exe -d $Distro -- python3 -c $writer $mode
if ($LASTEXITCODE -ne 0) { throw 'Failed to write private WSL configuration.' }
Write-Output 'Database remains AI_PMS. No SQL, migrations, containers or Cloudflare routes were changed.'
