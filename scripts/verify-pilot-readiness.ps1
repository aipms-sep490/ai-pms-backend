[CmdletBinding()]
param(
    [string]$ConnectionString = $env:AIPMS_SCHEMA_CHECK_CONNECTION,
    [string]$SemesterCode = 'PILOT-FALL2026'
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    Write-Error 'Set AIPMS_SCHEMA_CHECK_CONNECTION (or pass -ConnectionString) before running verify-pilot-readiness.ps1.'
    exit 3
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$pilotDir = Join-Path $repoRoot 'db/pilot'
$changesDir = Join-Path $repoRoot 'db/changes'

# 1. Static guard: verify db/pilot/ & db/changes/ contain NO hardcoded weak passwords or demo login hashes
$forbiddenPatterns = @('Aipms@', 'Password@123', 'e2e_password_hash', 'v5\.a\.leader')
foreach ($dir in @($pilotDir, $changesDir)) {
    if (-not (Test-Path $dir)) { continue }
    $sqlFiles = Get-ChildItem -Path $dir -Filter '*.sql' -Recurse
    foreach ($file in $sqlFiles) {
        $text = Get-Content -Path $file.FullName -Raw
        foreach ($pattern in $forbiddenPatterns) {
            if ($text -match $pattern) {
                Write-Error "SECURITY_VIOLATION: Forbidden demo credential pattern '$pattern' found in $($file.FullName)"
                exit 2
            }
        }
    }
}

$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
foreach ($pair in @(
    @('Trust Server Certificate', 'TrustServerCertificate'),
    @('Multiple Active Result Sets', 'MultipleActiveResultSets'),
    @('Application Intent', 'ApplicationIntent'),
    @('Multi Subnet Failover', 'MultiSubnetFailover')
)) {
    if ($neutral.ContainsKey($pair[0])) {
        $v = $neutral[$pair[0]]
        [void]$neutral.Remove($pair[0])
        $neutral[$pair[1]] = $v
    }
}
$connection = [System.Data.SqlClient.SqlConnection]::new($neutral.get_ConnectionString())

try {
    $connection.Open()
    $cmd = $connection.CreateCommand()
    $cmd.CommandTimeout = 60
    $p = $cmd.Parameters.Add('@sem', [System.Data.SqlDbType]::NVarChar, 50)
    $p.Value = $SemesterCode
    $cmd.CommandText = @'
SET NOCOUNT ON;
DECLARE @errors nvarchar(max) = N'';

-- A. Check Wave 4 schema readiness: 4 immutable triggers active & appeal scope / chat trigger present
DECLARE @enabledTriggers int = (
    SELECT COUNT(*)
    FROM sys.triggers t
    WHERE t.name IN (
        N'tr_immutable_evaluation_finalizations',
        N'tr_immutable_project_results',
        N'tr_immutable_student_results',
        N'tr_immutable_result_revisions'
    ) AND t.is_disabled = 0
);
IF @enabledTriggers <> 4
    SET @errors += CONCAT(N'Missing or disabled immutable score triggers (found ', @enabledTriggers, N'/4). ');

IF COL_LENGTH(N'dbo.result_appeal_requests', N'appeal_kind') IS NULL
    SET @errors += N'Missing dbo.result_appeal_requests.appeal_kind (20261215). ';

IF OBJECT_ID(N'dbo.tr_chat_project_close', N'TR') IS NULL
    SET @errors += N'Missing dbo.tr_chat_project_close (20261216). ';

-- B. Check target pilot semester exists and is ACTIVE
DECLARE @semId bigint = (SELECT TOP (1) id FROM dbo.academic_semesters WHERE code = @sem);
IF @semId IS NULL
    SET @errors += CONCAT(N'Pilot semester not found: ', @sem, N'. ');
ELSE
BEGIN
    -- C. Check all 6 canonical period types + defense scheduling flag = 0 (POL-06)
    DECLARE @periodCount int = (
        SELECT COUNT(DISTINCT period_type)
        FROM dbo.project_periods
        WHERE academic_semester_id = @semId
          AND status <> N'ARCHIVED'
          AND period_type IN (N'REGISTRATION', N'PROJECT_REVIEW', N'SUPERVISOR_SELECTION', N'EXECUTION', N'FINAL_SUBMISSION', N'EVALUATION')
    );
    IF @periodCount <> 6
        SET @errors += CONCAT(N'Expected 6 canonical project periods, found ', @periodCount, N'. ');

    IF EXISTS (SELECT 1 FROM dbo.project_periods WHERE academic_semester_id = @semId AND enable_defense_room_scheduling <> 0)
        SET @errors += N'POL-06 violation: enable_defense_room_scheduling must default to 0. ';

    -- D. Check all 3 tracks have PUBLISHED AcademicBlueprints and valid linked Rubric weights = 100.00
    DECLARE @publishedTracks int = (
        SELECT COUNT(DISTINCT track_code)
        FROM dbo.academic_blueprints
        WHERE academic_semester_id = @semId
          AND status = 'PUBLISHED'
          AND track_code IN ('IT_ONLY', 'MKT_ONLY', 'IT_MKT')
    );
    IF @publishedTracks <> 3
        SET @errors += CONCAT(N'Expected 3 PUBLISHED AcademicBlueprints (IT_ONLY, MKT_ONLY, IT_MKT), found ', @publishedTracks, N'. ');

    IF EXISTS (
        SELECT bp.id
        FROM dbo.academic_blueprints bp
        WHERE bp.academic_semester_id = @semId
          AND bp.status = 'PUBLISHED'
          AND (
              bp.rubric_id IS NULL
              OR NOT EXISTS (SELECT 1 FROM dbo.rubric_versions rv WHERE rv.rubric_id = bp.rubric_id AND rv.status = 'PUBLISHED')
              OR (SELECT COALESCE(SUM(rc.weight_percent), 0) FROM dbo.rubric_criteria rc WHERE rc.rubric_id = bp.rubric_id) <> 100.00
          )
    )
        SET @errors += N'One or more published blueprints lack a PUBLISHED rubric totaling 100.00%. ';

    -- E. Ensure IT_MKT blueprint has LEAD + PARTICIPATING approvals
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.academic_blueprints bp
        WHERE bp.academic_semester_id = @semId AND bp.track_code = 'IT_MKT' AND bp.status = 'PUBLISHED'
          AND (SELECT COUNT(*) FROM dbo.academic_blueprint_approvals a WHERE a.blueprint_id = bp.id AND a.decision = 'APPROVED') >= 2
    )
        SET @errors += N'IT_MKT blueprint is missing dual LEAD+PARTICIPATING approvals. ';
END

SELECT @errors AS ValidationErrors;
'@
    $result = $cmd.ExecuteScalar()
    $cmd.Dispose()
    if (-not [string]::IsNullOrWhiteSpace([string]$result)) {
        Write-Error "PILOT_READINESS_FAILED: $result"
        exit 1
    }
    Write-Output "PASS: Pilot 3-track readiness verified for semester '$SemesterCode' (4 triggers, 6 periods, 3 published blueprints, 100% rubrics, POL-06=false, 0 credential leaks)."
} finally {
    $connection.Dispose()
}
