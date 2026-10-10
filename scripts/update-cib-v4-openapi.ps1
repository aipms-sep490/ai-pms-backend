[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$openApiPath = Join-Path $repoRoot "docs\contracts\cib-v4\openapi.json"

Write-Host "Updating CIB v4 OpenAPI specification..."
Write-Host "Target path: $openApiPath"

# Choose an unused port for local API instance
$port = 59129
$apiUrl = "http://127.0.0.1:$port"
$swaggerUrl = "$apiUrl/swagger/v1/swagger.json"

Write-Host "Starting runtime AIPMS.Api instance on $apiUrl..."
$process = Start-Process -FilePath "dotnet" `
    -ArgumentList "run --project src/AIPMS.Api/AIPMS.Api.csproj -c Release --no-build --urls $apiUrl" `
    -WorkingDirectory $repoRoot `
    -PassThru

$rawSwagger = $null
try {
    # Poll until Swagger endpoint responds (up to 30 seconds)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $timeoutSeconds = 30
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSeconds) {
        try {
            $resp = Invoke-WebRequest -Uri $swaggerUrl -UseBasicParsing -TimeoutSec 2
            if ($resp.StatusCode -eq 200 -and $resp.Content.Length -gt 0) {
                $rawSwagger = $resp.Content
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if ([string]::IsNullOrWhiteSpace($rawSwagger)) {
        throw "Failed to retrieve OpenAPI specification from $swaggerUrl within $timeoutSeconds seconds."
    }

    Write-Host "OpenAPI specification retrieved ($($rawSwagger.Length) bytes)."
}
finally {
    if ($process -and -not $process.HasExited) {
        Write-Host "Stopping AIPMS.Api instance..."
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(5000)
    }
}

# Normalize JSON using native System.Text.Json via dotnet fsi to ensure exact serialization semantics
$tempRaw = Join-Path $repoRoot "temp_raw_swagger.json"
$tempScript = Join-Path $repoRoot "temp_format_swagger.fsx"

try {
    [System.IO.File]::WriteAllText($tempRaw, $rawSwagger)
    $fsiScriptContent = @"
open System.IO
open System.Text.Json

let raw = File.ReadAllText("""$tempRaw""")
use doc = JsonDocument.Parse(raw)
let opt = JsonSerializerOptions(WriteIndented = true)
let formatted = JsonSerializer.Serialize(doc.RootElement, opt)
File.WriteAllText("""$openApiPath""", formatted)
"@
    [System.IO.File]::WriteAllText($tempScript, $fsiScriptContent)
    & dotnet fsi $tempScript
    Write-Host "Successfully normalized and wrote OpenAPI specification to $openApiPath"
}
finally {
    if (Test-Path $tempRaw) { Remove-Item $tempRaw -Force }
    if (Test-Path $tempScript) { Remove-Item $tempScript -Force }
}
