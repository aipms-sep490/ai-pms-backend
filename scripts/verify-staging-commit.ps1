<#
.SYNOPSIS
    Automated verification of deployed commit SHA on AI-PMS staging environment.

.DESCRIPTION
    Queries the /api/v1/system endpoint on a target staging host, validates the response
    schema, checks that commitSha is a valid 40-character hexadecimal Git SHA (rejecting
    unpropagated versions like "1.0.0" and prefix matches), and strictly verifies that it
    matches the expected full 40-character Git commit SHA.

.PARAMETER ApiUrl
    Base URL of the AI-PMS API (default: https://api-staging.khaidz.com).

.PARAMETER ExpectedCommitSha
    The full 40-character hexadecimal Git commit SHA expected to be deployed.

.PARAMETER TimeoutSeconds
    HTTP request timeout in seconds (default: 15).

.PARAMETER Retries
    Number of retries upon connection failure or timeout (default: 3).

.PARAMETER RetryDelaySeconds
    Seconds to wait between retries (default: 3).

.EXAMPLE
    .\scripts\verify-staging-commit.ps1 -ExpectedCommitSha "3f71a30f1f03027be203025e17b2a6908d0ef84b"

.EXAMPLE
    .\scripts\verify-staging-commit.ps1 -ApiUrl "https://api-staging.khaidz.com" -ExpectedCommitSha "3f71a30f1f03027be203025e17b2a6908d0ef84b"
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$ApiUrl = "https://api-staging.khaidz.com",

    [Parameter(Position = 1, Mandatory = $true)]
    [string]$ExpectedCommitSha,

    [int]$TimeoutSeconds = 15,

    [int]$Retries = 3,

    [int]$RetryDelaySeconds = 3
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Pass {
    param([string]$Message)
    Write-Host "  [PASS] $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
}

# Trim and normalize inputs
$ApiUrl = $ApiUrl.TrimEnd('/')
$SystemEndpoint = "$ApiUrl/api/v1/system"
$ExpectedCommitSha = $ExpectedCommitSha.Trim()

Write-Host "==========================================================" -ForegroundColor DarkGray
Write-Host " AI-PMS Staging Deployment Verification" -ForegroundColor White
Write-Host " Target Endpoint: $SystemEndpoint" -ForegroundColor DarkGray
Write-Host " Expected SHA:    $ExpectedCommitSha" -ForegroundColor DarkGray
Write-Host "==========================================================" -ForegroundColor DarkGray

# Validate Expected SHA format upfront (strict 40 hex characters)
if ($ExpectedCommitSha -notmatch '^[0-9a-fA-F]{40}$') {
    Write-Fail "Parameter -ExpectedCommitSha ('$ExpectedCommitSha') must be a full 40-character hexadecimal Git SHA."
    exit 9
}

$attempt = 0
$statusCode = 0
$rawContent = ""

# Configure modern HttpClient compatible with PowerShell 5.1 and PowerShell 7
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$client = New-Object System.Net.Http.HttpClient($handler)
$client.Timeout = [System.TimeSpan]::FromSeconds($TimeoutSeconds)

while ($attempt -lt $Retries) {
    $attempt++
    Write-Step "Requesting $SystemEndpoint (Attempt $attempt of $Retries)..."
    try {
        $httpResponse = $client.GetAsync($SystemEndpoint).GetAwaiter().GetResult()
        $statusCode = [int]$httpResponse.StatusCode
        $rawContent = $httpResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()

        # Retry on transient server errors (500 Internal Server Error, 502 Bad Gateway, 503 Service Unavailable, 504 Gateway Timeout, 408 Request Timeout, 429 Too Many Requests)
        if ($statusCode -in @(408, 429, 500, 502, 503, 504) -and $attempt -lt $Retries) {
            Write-Warn "Attempt $attempt received transient HTTP $statusCode. Retrying..."
            Write-Host "  Waiting $RetryDelaySeconds seconds before retry..." -ForegroundColor DarkGray
            Start-Sleep -Seconds $RetryDelaySeconds
            continue
        }

        break
    }
    catch {
        $ex = $_
        Write-Warn "Attempt $attempt failed: $($ex.Exception.Message)"
        try { $client.Dispose() } catch { }
        $handler = New-Object System.Net.Http.HttpClientHandler
        $client = New-Object System.Net.Http.HttpClient($handler)
        $client.Timeout = [System.TimeSpan]::FromSeconds($TimeoutSeconds)
        if ($attempt -lt $Retries) {
            Write-Host "  Waiting $RetryDelaySeconds seconds before retry..." -ForegroundColor DarkGray
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }
}

if ($statusCode -eq 0 -or [string]::IsNullOrWhiteSpace($rawContent)) {
    Write-Fail "Could not establish connection to $SystemEndpoint after $Retries attempts."
    exit 1
}

# 1. Verify HTTP 200
if ($statusCode -ne 200) {
    Write-Fail "Expected HTTP 200 OK, got HTTP $statusCode."
    if ($rawContent -match 'Cloudflare|Error 1033|tunnel') {
        Write-Warn "Detected Cloudflare Gateway / Tunnel error page in response."
    }
    Write-Host "Response body snippet:`n$(if ($rawContent.Length -gt 500) { $rawContent.Substring(0, 500) + '...' } else { $rawContent })" -ForegroundColor DarkGray
    exit 2
}
Write-Pass "HTTP 200 OK received from $SystemEndpoint."

# 2. Parse JSON response
$json = $null
try {
    $json = $rawContent | ConvertFrom-Json
}
catch {
    Write-Fail "Response content is not valid JSON (e.g. HTML or plain text)."
    if ($rawContent -match '<html|<!DOCTYPE') {
        Write-Warn "Response appears to be an HTML error page."
    }
    exit 3
}

# 3. Check status property existence and value
if ($null -eq $json.status) {
    Write-Fail "Required property 'status' is missing from the JSON response."
    exit 4
}

if ($json.status -ne "ok") {
    Write-Fail "System status is '$($json.status)', expected 'ok'."
    exit 4
}
Write-Pass "System status is 'ok'."

# 4. Validate commitSha presence and Git SHA format
if ($null -eq $json.commitSha) {
    Write-Fail "Required property 'commitSha' is missing or null in the JSON response."
    exit 5
}

$actualSha = "$($json.commitSha)".Trim()

if ([string]::IsNullOrWhiteSpace($actualSha)) {
    Write-Fail "Field 'commitSha' is empty or whitespace."
    exit 5
}

# Check if returned commitSha is a build version fallback like "1.0.0" rather than a Git SHA
if ($actualSha -match '^\d+\.\d+\.\d+') {
    Write-Fail "Field 'commitSha' returned '$actualSha'. This is an assembly/version fallback, NOT a Git SHA."
    Write-Host "  Remediation: The running container was built without build-arg COMMIT_SHA." -ForegroundColor Yellow
    Write-Host '  Run on WSL: docker compose build --build-arg COMMIT_SHA=$(git rev-parse HEAD) api' -ForegroundColor Yellow
    exit 6
}

# Check strict 40-character hexadecimal Git SHA format (prefix matches rejected for official acceptance)
if ($actualSha -notmatch '^[0-9a-fA-F]{40}$') {
    Write-Fail "Field 'commitSha' ('$actualSha') is not a full 40-character hexadecimal Git SHA (length: $($actualSha.Length))."
    exit 7
}
Write-Pass "Commit SHA format is a full 40-character hexadecimal Git SHA: $actualSha"

# 5. Strict comparison with expected SHA (full 40-character match, case-insensitive)
$expectedNormalized = $ExpectedCommitSha.ToLowerInvariant()
$actualNormalized = $actualSha.ToLowerInvariant()

if ($actualNormalized -ne $expectedNormalized) {
    Write-Fail "SHA MISMATCH!"
    Write-Host "  Expected SHA: $ExpectedCommitSha" -ForegroundColor Red
    Write-Host "  Actual SHA:   $actualSha" -ForegroundColor Red
    Write-Host "  Difference:   The staging server is running a different commit than expected." -ForegroundColor Yellow
    Write-Host "  Remediation:  Khải needs to pull the target branch and rebuild the container with COMMIT_SHA." -ForegroundColor Yellow
    exit 8
}

Write-Pass "Commit SHA strictly matches expected value ($actualSha == $ExpectedCommitSha)."

Write-Host "==========================================================" -ForegroundColor DarkGray
Write-Host " STAGING COMMIT VERIFICATION: SUCCESS (PASS)" -ForegroundColor Green
Write-Host " Deployed API:    $ApiUrl" -ForegroundColor White
Write-Host " Verified SHA:    $actualSha" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor DarkGray

exit 0
