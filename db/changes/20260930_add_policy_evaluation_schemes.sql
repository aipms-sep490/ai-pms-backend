/* Baseline PR4. Additive and rerunnable. Historical scope is never inferred. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.period_policy_versions',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.period_policy_versions (
  id BIGINT IDENTITY PRIMARY KEY, project_period_id BIGINT NOT NULL REFERENCES dbo.project_periods(id),
  version INT NOT NULL, status NVARCHAR(20) NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','LOCKED')),
  effective_from DATETIME2(7) NOT NULL, effective_to DATETIME2(7) NOT NULL,
  snapshot_json NVARCHAR(MAX) NOT NULL CHECK(ISJSON(snapshot_json)=1), concurrency_token UNIQUEIDENTIFIER NOT NULL,
  created_by BIGINT NULL REFERENCES dbo.users(id), created_at DATETIME2(7) NOT NULL,
  CONSTRAINT uq_period_policy_version UNIQUE(project_period_id,version), CHECK(effective_from < effective_to)
 );
 CREATE TABLE dbo.period_policy_usages (
  id BIGINT IDENTITY PRIMARY KEY, policy_version_id BIGINT NOT NULL REFERENCES dbo.period_policy_versions(id),
  entity_type NVARCHAR(40) NOT NULL, entity_id BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL,
  CONSTRAINT uq_period_policy_usage UNIQUE(entity_type,entity_id)
 );
 CREATE INDEX ix_policy_usage_version ON dbo.period_policy_usages(policy_version_id);
END;
IF OBJECT_ID(N'dbo.evaluation_schemes',N'U') IS NULL
BEGIN
 CREATE TABLE dbo.evaluation_schemes (
  id BIGINT IDENTITY PRIMARY KEY, root_id BIGINT NULL REFERENCES dbo.evaluation_schemes(id), version INT NOT NULL,
  project_id BIGINT NOT NULL REFERENCES dbo.projects(id), project_period_id BIGINT NOT NULL REFERENCES dbo.project_periods(id),
  name NVARCHAR(200) NOT NULL, status NVARCHAR(20) NOT NULL CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
  pass_threshold DECIMAL(5,2) NOT NULL CHECK(pass_threshold BETWEEN 0 AND 10), concurrency_token UNIQUEIDENTIFIER NOT NULL,
  policy_version_id BIGINT NULL REFERENCES dbo.period_policy_versions(id), students_json NVARCHAR(MAX) NOT NULL,
  registration_snapshot_json NVARCHAR(MAX) NOT NULL,
  calculation_rule NVARCHAR(100) NOT NULL,
  created_by BIGINT NOT NULL REFERENCES dbo.users(id), created_at DATETIME2(7) NOT NULL,
  published_by BIGINT NULL REFERENCES dbo.users(id), published_at DATETIME2(7) NULL,
  CONSTRAINT uq_evaluation_scheme_version UNIQUE(project_id,version)
 );
 CREATE UNIQUE INDEX uq_evaluation_scheme_published ON dbo.evaluation_schemes(project_id) WHERE status='PUBLISHED';
 CREATE TABLE dbo.evaluation_scheme_components (
  id BIGINT IDENTITY PRIMARY KEY, scheme_id BIGINT NOT NULL REFERENCES dbo.evaluation_schemes(id) ON DELETE CASCADE,
  name NVARCHAR(200) NOT NULL, scope NVARCHAR(20) NOT NULL CHECK(scope IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')),
  major_id BIGINT NULL REFERENCES dbo.majors(id), rubric_id BIGINT NOT NULL REFERENCES dbo.rubrics(id),
  project_weight_percent DECIMAL(9,4) NOT NULL CHECK(project_weight_percent BETWEEN 0 AND 100),
  student_weight_percent DECIMAL(9,4) NOT NULL CHECK(student_weight_percent BETWEEN 0 AND 100),
  required_evaluators INT NOT NULL CHECK(required_evaluators BETWEEN 1 AND 20),
  CHECK((scope='COMMON' AND major_id IS NULL) OR (scope IN ('MAJOR_SPECIFIC','INDIVIDUAL') AND major_id IS NOT NULL)),
  CHECK(scope<>'INDIVIDUAL' OR project_weight_percent=0)
 );
 CREATE TABLE dbo.student_results (
  id BIGINT IDENTITY PRIMARY KEY, project_id BIGINT NOT NULL REFERENCES dbo.projects(id),
  student_id BIGINT NOT NULL REFERENCES dbo.users(id), major_id BIGINT NOT NULL REFERENCES dbo.majors(id),
  scheme_id BIGINT NOT NULL REFERENCES dbo.evaluation_schemes(id), total_score DECIMAL(5,2) NOT NULL CHECK(total_score BETWEEN 0 AND 10),
  pass_threshold DECIMAL(5,2) NOT NULL, outcome NVARCHAR(20) NOT NULL CHECK(outcome IN ('PASSED','FAILED')),
  calculation_rule NVARCHAR(100) NOT NULL, published_by BIGINT NOT NULL REFERENCES dbo.users(id),
  published_at DATETIME2(7) NOT NULL, snapshot_json NVARCHAR(MAX) NOT NULL CHECK(ISJSON(snapshot_json)=1),
  CONSTRAINT uq_student_result UNIQUE(project_id,student_id)
 );
 CREATE TABLE dbo.student_result_evaluations (
  result_id BIGINT NOT NULL REFERENCES dbo.student_results(id), evaluation_id BIGINT NOT NULL REFERENCES dbo.evaluations(id),
  PRIMARY KEY(result_id,evaluation_id)
 );
END;
IF COL_LENGTH('dbo.evaluation_assignments','scope') IS NULL
BEGIN
 ALTER TABLE dbo.evaluation_assignments ADD scope NVARCHAR(20) NOT NULL CONSTRAINT df_evaluation_scope DEFAULT('UNKNOWN'),
  major_id BIGINT NULL REFERENCES dbo.majors(id), student_id BIGINT NULL REFERENCES dbo.users(id),
  component_id BIGINT NULL REFERENCES dbo.evaluation_scheme_components(id),
  policy_version_id BIGINT NULL REFERENCES dbo.period_policy_versions(id), scope_snapshot_json NVARCHAR(MAX) NULL;
END;
EXEC(N'IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_evaluation_assignments_active'')
 DROP INDEX uq_evaluation_assignments_active ON dbo.evaluation_assignments;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_scoped_evaluation_assignment'')
 CREATE UNIQUE INDEX uq_scoped_evaluation_assignment ON dbo.evaluation_assignments(project_id,component_id,student_id,evaluator_id) WHERE status=''ACTIVE'' AND component_id IS NOT NULL;
 IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(''dbo.evaluation_assignments'') AND name=''uq_legacy_evaluation_assignment'')
 CREATE UNIQUE INDEX uq_legacy_evaluation_assignment ON dbo.evaluation_assignments(project_id,evaluator_id,evaluation_type) WHERE status=''ACTIVE'' AND component_id IS NULL;');
IF OBJECT_ID('dbo.ck_evaluation_assignment_scope','C') IS NULL
 EXEC(N'ALTER TABLE dbo.evaluation_assignments ADD CONSTRAINT ck_evaluation_assignment_scope CHECK(
 (scope=''UNKNOWN'' AND component_id IS NULL AND major_id IS NULL AND student_id IS NULL) OR
 (component_id IS NOT NULL AND policy_version_id IS NOT NULL AND scope_snapshot_json IS NOT NULL AND (
 (scope=''COMMON'' AND major_id IS NULL AND student_id IS NULL) OR
 (scope=''MAJOR_SPECIFIC'' AND major_id IS NOT NULL AND student_id IS NULL) OR
 (scope=''INDIVIDUAL'' AND major_id IS NOT NULL AND student_id IS NOT NULL))));');
IF COL_LENGTH('dbo.evaluation_schemes','calculation_rule') IS NULL
    ALTER TABLE dbo.evaluation_schemes ADD calculation_rule NVARCHAR(100) NOT NULL
        CONSTRAINT df_evaluation_scheme_calculation_rule DEFAULT('COMPONENT_EQUAL_EVALUATOR_MEAN_WEIGHTED_10_AWAY_2DP_V1');
COMMIT;
