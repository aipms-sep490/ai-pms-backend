[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory,
    [ValidateRange(1, 168)][int]$MaxAgeHours = 24
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# These files are emitted only after the scenario's HTTP and database assertions pass.
$required = [ordered]@{
    'expansion-working-agreement' = @{ versions = 2; acceptanceAuditExactlyOnce = $true; successorInheritsConsent = $false; migrationRerunPreservesHistory = $true }
    'expansion-working-agreement-activation' = @{ incompleteStatusCode = 422; projectStatus = 'ACTIVE'; activeAssignments = 1; currentRosterConsentRequired = $true }
    'expansion-supervisor-recommendations' = @{ audit = $true; noAssignment = $true; staleCapacityDenied = $true; rankingBeforePaging = $true }
    'expansion-peer-lifecycle' = @{ submitted = 2; submissionAuditExactlyOnce = $true; officialScoreWrites = 0 }
    'expansion-result-revision-foundation' = @{ publicationViaApi = $true; correctionViaInternalStore = $true; auditRollbackVerified = $true; concurrentPublishWinners = 1; historyRedacted = $true }
    'expansion-appeal-lifecycle' = @{ resultVersion = 2; concurrentWinners = 1; auditRollback = $true; originalResultsUnchanged = $true }
    'expansion-industry-lifecycle' = @{ assignmentRevoked = $true; shareRevoked = $true; feedbackVersions = 2; officialResults = 0 }
}
$projectionFields = @('id', 'status', 'scope', 'stage', 'totalScore', 'canPublish', 'blockers',
    'generationStatus', 'insufficientEvidence', 'errorCode', 'runId', 'projectCount',
    'openTasks', 'overdueTasks', 'complete')

function Assert-SafeProjection($value) {
    if ($null -eq $value) { return }
    if ($value -isnot [System.Collections.IDictionary]) { throw 'HTTP projection must be an object or null.' }
    foreach ($key in $value.Keys) {
        if ($key -cnotin $projectionFields) { throw 'HTTP projection contains a non-allowlisted field.' }
        foreach ($item in @($value[$key])) {
            if ($item -is [System.Collections.IDictionary] -or $item -is [array]) {
                throw 'Nested HTTP payloads are not accepted as redacted evidence.'
            }
        }
    }
}

$now = [DateTimeOffset]::UtcNow
foreach ($scenario in $required.Keys) {
    $path = Join-Path $Directory ($scenario + '.json')
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing evidence: $scenario" }
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
    if ($report.scenario -cne $scenario -or $report.synthetic -isnot [bool] -or !$report.synthetic -or
        $report.isolatedDatabase -isnot [bool] -or !$report.isolatedDatabase -or
        $report.passed -isnot [bool] -or !$report.passed) { throw "Unqualified evidence: $scenario" }
    $captured = [DateTimeOffset]$report.capturedAtUtc
    if ($captured -gt $now.AddMinutes(5) -or $captured -lt $now.AddHours(-$MaxAgeHours)) {
        throw "Stale/future evidence: $scenario"
    }
    if ($report.authentication -ne 'test JWT; persisted resource authorization exercised' -or
        $report.storage -ne 'in-memory storage adapter where files are used') {
        throw "Missing test adapter disclosure: $scenario"
    }
    if ($report.exchanges -isnot [array] -or $report.exchanges.Count -lt 2) { throw "Missing HTTP lifecycle: $scenario" }
    $hasSuccessfulMutation = $false
    foreach ($exchange in $report.exchanges) {
        if ($exchange.method -cnotin @('GET', 'POST', 'PUT', 'PATCH', 'DELETE') -or
            $exchange.path -cnotmatch '^/api/v1/[A-Za-z0-9/_-]+$' -or
            $exchange.statusCode -lt 200 -or $exchange.statusCode -gt 599) { throw "Invalid HTTP evidence: $scenario" }
        $correlation = [Guid]::Empty
        if (![Guid]::TryParse($exchange.correlationId, [ref]$correlation)) { throw "Missing correlation ID: $scenario" }
        Assert-SafeProjection $exchange.request
        Assert-SafeProjection $exchange.response
        if ($exchange.method -ne 'GET' -and $exchange.statusCode -lt 300) { $hasSuccessfulMutation = $true }
    }
    if (!$hasSuccessfulMutation) { throw "No successful HTTP mutation: $scenario" }
    foreach ($field in $required[$scenario].Keys) {
        $expected = $required[$scenario][$field]
        $actual = $report.readback[$field]
        if ($null -eq $actual -or ($expected -is [bool] -and $actual -isnot [bool]) -or
            $actual -cne $expected) { throw "Missing/failed DB assertion: $scenario/$field" }
    }
}
Write-Output "Verified $($required.Count) fresh isolated expansion reports with HTTP and DB readback. Browser/provider acceptance remains separate."
