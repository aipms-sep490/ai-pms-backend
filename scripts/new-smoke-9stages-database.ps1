[CmdletBinding()]
param(
    [string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION,
    [string]$OutputPath = (Join-Path ([IO.Path]::GetTempPath()) ('aipms-smoke-9stages-' + [Guid]::NewGuid().ToString('N') + '.json')),
    [string]$DatabaseName = ('AI_PMS_E2E_' + [Guid]::NewGuid().ToString('N')),
    [switch]$Drop
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw 'Supply AIPMS_TEST_SQL_CONNECTION (master connection string) before running new-smoke-9stages-database.ps1.'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$e2eScript = Join-Path $PSScriptRoot 'new-e2e-database.ps1'

if ($Drop) {
    & $e2eScript -ConnectionString $ConnectionString -DatabaseName $DatabaseName -Drop
    return
}

if ([string]::IsNullOrWhiteSpace($env:AIPMS_E2E_PASSWORD) -or $env:AIPMS_E2E_PASSWORD.Length -lt 12) {
    $env:AIPMS_E2E_PASSWORD = [Guid]::NewGuid().ToString('N') + 'Aa1!'
}

# 1. Provision isolated disposable E2E database (schema + all migrations + base E2E actors & hashed password)
& $e2eScript -ConnectionString $ConnectionString -DatabaseName $DatabaseName | Out-Null

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
$sqlBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
$sqlBuilder['Initial Catalog'] = $DatabaseName

$pilotSeedPath = Join-Path $repoRoot 'db/pilot/01_seed_3track_pilot_config.sql'
$pilotSeedSql = [IO.File]::ReadAllText($pilotSeedPath, [System.Text.Encoding]::UTF8)

$connection = [System.Data.SqlClient.SqlConnection]::new($sqlBuilder.ConnectionString)
try {
    $connection.Open()

    # 2. Apply idempotent 3-Track Pilot Config (IT_ONLY, MKT_ONLY, IT_MKT)
    $seedCmd = $connection.CreateCommand()
    $seedCmd.CommandTimeout = 120
    $seedCmd.CommandText = $pilotSeedSql
    [void]$seedCmd.ExecuteNonQuery()
    $seedCmd.Dispose()

    # 3. Seed non-secret role fixtures tied to the runtime E2E password hash already in dbo.users
    $actorSql = @'
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

DECLARE @pwHash nvarchar(500) = (SELECT TOP (1) password_hash FROM dbo.users WHERE email = N'admin@e2e.invalid');
IF @pwHash IS NULL THROW 51015, N'Base admin@e2e.invalid hash not found.', 1;

DECLARE @orgId bigint = (SELECT id FROM dbo.organizations WHERE code = N'PILOT');
DECLARE @deptIt bigint = (SELECT id FROM dbo.departments WHERE organization_id = @orgId AND code = N'IT');
DECLARE @deptMkt bigint = (SELECT id FROM dbo.departments WHERE organization_id = @orgId AND code = N'MKT');
DECLARE @majorSe bigint = (SELECT id FROM dbo.majors WHERE department_id = @deptIt AND code = N'SE');
DECLARE @majorMkt bigint = (SELECT id FROM dbo.majors WHERE department_id = @deptMkt AND code = N'MKT');

DECLARE @actors TABLE(
    email nvarchar(255),
    full_name nvarchar(200),
    dept_id bigint,
    major_id bigint,
    role_code nvarchar(30),
    student_code nvarchar(50)
);
INSERT @actors VALUES
(N'smoke.staff.it@e2e.invalid',     N'Smoke IT Staff',        @deptIt,  NULL,      N'DEPARTMENT_STAFF', NULL),
(N'smoke.staff.mkt@e2e.invalid',    N'Smoke MKT Staff',       @deptMkt, NULL,      N'DEPARTMENT_STAFF', NULL),
(N'smoke.sup.it@e2e.invalid',       N'Smoke IT Supervisor',   @deptIt,  NULL,      N'LECTURER',         NULL),
(N'smoke.mentor.mkt@e2e.invalid',   N'Smoke MKT Mentor',      @deptMkt, NULL,      N'LECTURER',         NULL),
(N'smoke.gate.rev1@e2e.invalid',    N'Smoke Gate Reviewer 1', @deptIt,  NULL,      N'LECTURER',         NULL),
(N'smoke.gate.rev2@e2e.invalid',    N'Smoke Gate Reviewer 2', @deptMkt, NULL,      N'LECTURER',         NULL),
(N'smoke.eval1@e2e.invalid',        N'Smoke Evaluator 1',     @deptIt,  NULL,      N'LECTURER',         NULL),
(N'smoke.eval2@e2e.invalid',        N'Smoke Evaluator 2',     @deptMkt, NULL,      N'LECTURER',         NULL),
(N'smoke.appeal.rev@e2e.invalid',   N'Smoke Appeal Reviewer', @deptIt,  NULL,      N'LECTURER',         NULL),
(N'smoke.it.leader@e2e.invalid',    N'Smoke IT Leader',       @deptIt,  @majorSe,  N'STUDENT',          N'SMK-IT-01'),
(N'smoke.it.member@e2e.invalid',    N'Smoke IT Member',       @deptIt,  @majorSe,  N'STUDENT',          N'SMK-IT-02'),
(N'smoke.it.member2@e2e.invalid',   N'Smoke IT Member 2',     @deptIt,  @majorSe,  N'STUDENT',          N'SMK-IT-03'),
(N'smoke.it2.leader@e2e.invalid',   N'Smoke IT2 Leader',      @deptIt,  @majorSe,  N'STUDENT',          N'SMK-IT-04'),
(N'smoke.it2.member@e2e.invalid',   N'Smoke IT2 Member',      @deptIt,  @majorSe,  N'STUDENT',          N'SMK-IT-05'),
(N'smoke.mkt.leader@e2e.invalid',   N'Smoke MKT Leader',      @deptMkt, @majorMkt, N'STUDENT',          N'SMK-MK-01'),
(N'smoke.mkt.member@e2e.invalid',   N'Smoke MKT Member',      @deptMkt, @majorMkt, N'STUDENT',          N'SMK-MK-02');

MERGE dbo.users AS t
USING @actors AS s ON t.email = s.email
WHEN NOT MATCHED THEN
    INSERT (department_id, major_id, email, password_hash, full_name, student_code, status, academic_profile_status)
    VALUES (s.dept_id, s.major_id, s.email, @pwHash, s.full_name, s.student_code, N'ACTIVE', N'VERIFIED');

INSERT dbo.user_roles (user_id, role_id)
SELECT u.id, r.id
FROM @actors a
JOIN dbo.users u ON u.email = a.email
JOIN dbo.roles r ON r.code = a.role_code
WHERE NOT EXISTS (SELECT 1 FROM dbo.user_roles x WHERE x.user_id = u.id AND x.role_id = r.id);

INSERT dbo.supervisor_profiles (user_id, max_active_projects)
SELECT u.id, 10
FROM @actors a
JOIN dbo.users u ON u.email = a.email
WHERE a.role_code = N'LECTURER'
  AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_profiles sp WHERE sp.user_id = u.id);

DECLARE @semId bigint = (SELECT id FROM dbo.academic_semesters WHERE organization_id = @orgId AND code = N'PILOT-FALL2026');
DECLARE @nowUtc datetime2(0) = SYSUTCDATETIME();
DECLARE @winStart datetime2(0) = DATEADD(DAY, -14, @nowUtc);
DECLARE @winEnd datetime2(0) = DATEADD(DAY, 30, @nowUtc);

-- Keep isolated E2E semester/period windows active relative to runtime UTC clock so live API E2E runs remain date-independent
UPDATE dbo.academic_semesters
SET start_date = CAST(DATEADD(DAY, -21, @nowUtc) AS date),
    end_date = CAST(DATEADD(DAY, 120, @nowUtc) AS date),
    status = N'ACTIVE'
WHERE id = @semId;

UPDATE dbo.project_periods
SET start_at = CASE period_type
        WHEN 'REGISTRATION' THEN DATEADD(DAY, -14, @nowUtc)
        WHEN 'PROJECT_REVIEW' THEN DATEADD(DAY, -10, @nowUtc)
        WHEN 'SUPERVISOR_SELECTION' THEN DATEADD(DAY, -7, @nowUtc)
        WHEN 'EXECUTION' THEN DATEADD(DAY, -5, @nowUtc)
        WHEN 'FINAL_SUBMISSION' THEN DATEADD(DAY, -2, @nowUtc)
        ELSE DATEADD(DAY, -1, @nowUtc)
    END,
    end_at = @winEnd,
    status = N'ACTIVE'
WHERE academic_semester_id = @semId;

UPDATE dbo.period_policy_versions
SET effective_from = DATEADD(DAY, -21, @nowUtc),
    effective_to = DATEADD(DAY, 120, @nowUtc)
WHERE project_period_id IN (SELECT id FROM dbo.project_periods WHERE academic_semester_id = @semId);

DECLARE @periodsJson nvarchar(max) = (SELECT id, code, period_type AS periodType, status FROM dbo.project_periods WHERE academic_semester_id = @semId FOR JSON PATH);
DECLARE @blueprintsJson nvarchar(max) = (SELECT id, track_code AS trackCode, code, status, rubric_id AS rubricId FROM dbo.academic_blueprints WHERE academic_semester_id = @semId AND status = 'PUBLISHED' FOR JSON PATH);
DECLARE @supProfilesJson nvarchar(max) = (SELECT sp.id AS profileId, sp.user_id AS userId, u.email FROM dbo.supervisor_profiles sp JOIN dbo.users u ON u.id = sp.user_id WHERE u.email LIKE N'smoke.%' FOR JSON PATH);
DECLARE @actorsJson nvarchar(max) = (SELECT u.id, u.email, u.full_name AS fullName, a.role_code AS role, u.department_id AS departmentId, u.major_id AS majorId FROM @actors a JOIN dbo.users u ON u.email = a.email FOR JSON PATH);

DECLARE @payload nvarchar(max) = (
    SELECT
        @DatabaseName AS databaseName,
        @orgId AS organizationId,
        @semId AS semesterId,
        @deptIt AS itDepartmentId,
        @deptMkt AS mktDepartmentId,
        @majorSe AS seMajorId,
        @majorMkt AS mktMajorId,
        JSON_QUERY(@periodsJson) AS periods,
        JSON_QUERY(@blueprintsJson) AS blueprints,
        JSON_QUERY(@supProfilesJson) AS supervisorProfiles,
        JSON_QUERY(@actorsJson) AS actors
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER
);

COMMIT TRANSACTION;
SELECT @payload;
'@
    $cmd = $connection.CreateCommand()
    $cmd.CommandTimeout = 60
    $cmd.CommandText = $actorSql
    $pDb = $cmd.Parameters.Add('@DatabaseName', [System.Data.SqlDbType]::NVarChar, 128)
    $pDb.Value = $DatabaseName
    $json = [string]$cmd.ExecuteScalar()
    $cmd.Dispose()

    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
    [IO.File]::WriteAllText($OutputPath, $json, [System.Text.UTF8Encoding]::new($false))
    Write-Output "READY: $OutputPath (Database=$DatabaseName)"
} finally {
    $connection.Dispose()
}
