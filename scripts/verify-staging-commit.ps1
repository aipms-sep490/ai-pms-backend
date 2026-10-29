<#
.SYNOPSIS
    Automated verification of deployed commit SHA on AI-PMS staging environment.

.DESCRIPTION
    Queries the /api/v1/system endpoint on a target staging host, validates the response
    schema, checks that commitSha is a valid Git commit SHA (rejecting unpropagated versions
    like "1.0.0"), and verifies that it matches the expected Git commit SHA.

.PARAMETER ApiUrl
    Base URL of the AI-PMS API (default: https://api-staging.khaidz.com).

.PARAMETER ExpectedCommitSha
    The full or abbreviated Git commit SHA expected to be deployed.

.PARAMETER TimeoutSeconds
    HTTP request timeout in seconds (default: 15).

.PARAMETER Retries
    Number of retries upon connection failure or timeout (default: 3).

.PARAMETER RetryDelaySeconds
    Seconds to wait between retries (default: 3).

.EXAMPLE
    .\scripts\verify-staging-commit.ps1 -ExpectedCommitSha "3f71a30f1f03027be203025e17b2a6908d0ef84b"

.EXAMPLE
    .\scripts\verify-staging-commit.ps1 -ApiUrl "https://api-staging.khaidz.com" -ExpectedCommitSha "3f71a30"
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

$attempt = 0
$response = $null
$statusCode = 0
$rawContent = ""

# Configure modern HttpClient
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
        break
    }
    catch {
        $ex = $_
        Write-Warn "Attempt $attempt failed: $($ex.Exception.Message)"
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
    Write-Host "Response body:`n$rawContent" -ForegroundColor DarkGray
    exit 2
}
Write-Pass "HTTP 200 OK received from $SystemEndpoint."

# 2. Parse JSON response
$json = $null
try {
    $json = $rawContent | ConvertFrom-Json
}
catch {
    Write-Fail "Response content is not valid JSON."
    Write-Host "Raw content:`n$rawContent" -ForegroundColor DarkGray
    exit 3
}

# 3. Check status == "ok"
if ($json.status -ne "ok") {
    Write-Fail "System status is '$($json.status)', expected 'ok'."
    exit 4
}
Write-Pass "System status is '$($json.status)'."

# 4. Validate commitSha presence and Git SHA format
$actualSha = "$($json.commitSha)".Trim()

if ([string]::IsNullOrWhiteSpace($actualSha)) {
    Write-Fail "Field 'commitSha' is missing or empty in system response."
    Write-Host "Payload: $($rawContent)" -ForegroundColor DarkGray
    exit 5
}

# Check if returned commitSha is a build version fallback like "1.0.0" rather than a Git SHA
if ($actualSha -match '^\d+\.\d+\.\d+') {
    Write-Fail "Field 'commitSha' returned '$actualSha'. This is an assembly/version fallback, NOT a Git SHA."
    Write-Host "  Remediation: The running container was built without build-arg COMMIT_SHA." -ForegroundColor Yellow
    Write-Host '  Run on WSL: docker compose build --build-arg COMMIT_SHA=$(git rev-parse HEAD) api' -ForegroundColor Yellow
    exit 6
}

# Check valid hexadecimal Git SHA (7 to 40 hex characters)
if ($actualSha -notmatch '^[0-9a-fA-F]{7,40}$') {
    Write-Fail "Field 'commitSha' ('$actualSha') is not a valid Git SHA (expected 7-40 hexadecimal characters)."
    exit 7
}
Write-Pass "Commit SHA format is valid hexadecimal: $actualSha"

# 5. Compare with expected SHA
$expectedNormalized = $ExpectedCommitSha.ToLowerInvariant()
$actualNormalized = $actualSha.ToLowerInvariant()

$isMatch = $false
if ($actualNormalized -eq $expectedNormalized) {
    $isMatch = $true
}
elseif ($expectedNormalized.Length -lt $actualNormalized.Length -and $actualNormalized.StartsWith($expectedNormalized)) {
    $isMatch = $true
}
elseif ($actualNormalized.Length -lt $expectedNormalized.Length -and $expectedNormalized.StartsWith($actualNormalized)) {
    $isMatch = $true
}

if (-not $isMatch) {
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
