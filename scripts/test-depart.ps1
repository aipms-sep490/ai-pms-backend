[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path ([IO.Path]::GetTempPath()) ('aipms-depart-' + [Guid]::NewGuid().ToString('N'))),
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:AIPMS_TEST_SQL_CONNECTION)) {
    throw 'Set AIPMS_TEST_SQL_CONNECTION through the process environment. The fixtures create and clean up their own databases.'
}
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests/AIPMS.IntegrationTests/AIPMS.IntegrationTests.csproj'
[void](New-Item -ItemType Directory -Path $ResultsDirectory -Force)
$results = @()
Push-Location $root
try {
    $sourceCommit = & git rev-parse HEAD
    $sourceDirty = [bool](& git status --porcelain)
    if (-not $SkipBuild) {
        & dotnet build $project -c Release -p:TreatWarningsAsErrors=true *> (Join-Path $ResultsDirectory 'build.log')
        if ($LASTEXITCODE -ne 0) { throw 'Release build failed; see build.log.' }
    }
    # SQL containers/fixtures must not compete for all the memory on a developer host.
    foreach ($suite in @('TeamEndpointTests', 'InterdisciplinaryWorkflowTests', 'SupervisorRequestEndpointTests',
        'SupervisorAssignmentEndpointTests', 'SupervisorCandidateEndpointTests', 'PolicyEvaluationEndpointTests', 'FinalSubmissionEndpointTests',
        'DashboardRepositoryTests', 'GovernanceMigrationTests', 'DepartOpenApiTests', 'ProjectRepositoryTests',
        'ProjectEndpointTests', 'ProjectRequirementsEndpointTests', 'DeliverableEndpointTests', 'EvaluationDraftEndpointTests',
        'AcademicStructureEndpointTests', 'SemesterEndpointTests', 'WorkflowContextTests')) {
        & dotnet test $project -c Release --no-build --filter "FullyQualifiedName~.$suite." `
            --results-directory $ResultsDirectory --logger "trx;LogFileName=$suite.trx" *> (Join-Path $ResultsDirectory "$suite.log")
        $exitCode = $LASTEXITCODE
        $trxPath = Join-Path $ResultsDirectory "$suite.trx"
        $executed = 0
        $failed = 0
        if (Test-Path $trxPath) {
            [xml]$trx = Get-Content $trxPath -Raw
            $executed = [int]$trx.TestRun.ResultSummary.Counters.executed
            $failed = [int]$trx.TestRun.ResultSummary.Counters.failed
        }
        $passed = $exitCode -eq 0 -and $executed -gt 0 -and $failed -eq 0
        $results += [pscustomobject]@{ suite = $suite; passed = $passed; executed = $executed; failed = $failed }
        Write-Output "$suite : passed=$passed, executed=$executed, failed=$failed"
    }
    $bootstrapPassed = $false
    try {
        & (Join-Path $PSScriptRoot 'test-e2e-bootstrap.ps1') *> (Join-Path $ResultsDirectory 'migration-rerun.log')
        $bootstrapPassed = $true
    } catch {
        $_ | Out-String | Add-Content (Join-Path $ResultsDirectory 'migration-rerun.log')
        Write-Warning 'Isolated migration rerun failed; see migration-rerun.log.'
    }
    $lifecyclePassed = $false
    $lifecycleTrx = Join-Path $ResultsDirectory 'InterdisciplinaryWorkflowTests.trx'
    if (Test-Path $lifecycleTrx) {
        [xml]$lifecycleResults = Get-Content $lifecycleTrx -Raw
        $journeys = @($lifecycleResults.TestRun.Results.UnitTestResult | Where-Object { $_.testName -like '*Depart_D08_qualification_to_archive*' })
        $lifecyclePassed = $journeys.Count -eq 2 -and @($journeys | Where-Object { $_.outcome -ne 'Passed' }).Count -eq 0
    }
    [pscustomobject]@{
        utc = [DateTime]::UtcNow.ToString('o')
        commit = $sourceCommit
        worktreeDirty = $sourceDirty
        buildSkipped = [bool]$SkipBuild
        suites = $results
        migrationRerunPassed = $bootstrapPassed
        completeLifecycleAccepted = $lifecyclePassed
    } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ResultsDirectory 'report.json') -Encoding utf8
    if (-not $bootstrapPassed -or -not $lifecyclePassed -or @($results | Where-Object { -not $_.passed }).Count -gt 0) {
        throw "DEPART validation failed. See $ResultsDirectory"
    }
    Write-Output "DEPART regression, migration rerun and both full API lifecycles passed. Reports: $ResultsDirectory."
} finally { Pop-Location }
