SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.result_correction_bundles','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_correction_bundles (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_result_correction_bundles PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_correction_bundle_project REFERENCES dbo.projects(id),
  created_by BIGINT NOT NULL CONSTRAINT fk_correction_bundle_creator REFERENCES dbo.users(id),
  created_at DATETIME2(0) NOT NULL, reason NVARCHAR(2000) NOT NULL,
  status VARCHAR(20) NOT NULL CONSTRAINT ck_correction_bundle_status CHECK(status IN ('DRAFT','PUBLISHED')),
  base_stamp CHAR(64) NOT NULL, concurrency_token UNIQUEIDENTIFIER NOT NULL,
  published_by BIGINT NULL CONSTRAINT fk_correction_bundle_publisher REFERENCES dbo.users(id),
  published_at DATETIME2(0) NULL,
  CONSTRAINT ck_correction_separation CHECK(published_by IS NULL OR published_by <> created_by),
  CONSTRAINT ck_correction_publication CHECK((status='DRAFT' AND published_by IS NULL AND published_at IS NULL)
    OR (status='PUBLISHED' AND published_by IS NOT NULL AND published_at IS NOT NULL))
 );
 CREATE INDEX ix_correction_bundle_project ON dbo.result_correction_bundles(project_id,id DESC);
END;
IF OBJECT_ID('dbo.result_score_amendments','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_score_amendments (
  bundle_id BIGINT NOT NULL CONSTRAINT fk_amendment_bundle REFERENCES dbo.result_correction_bundles(id),
  evaluation_id BIGINT NOT NULL CONSTRAINT fk_amendment_evaluation REFERENCES dbo.evaluations(id),
  criterion_id BIGINT NOT NULL CONSTRAINT fk_amendment_criterion REFERENCES dbo.rubric_criteria(id),
  score DECIMAL(9,2) NOT NULL CONSTRAINT ck_amendment_score CHECK(score >= 0), reason NVARCHAR(2000) NOT NULL,
  CONSTRAINT pk_result_score_amendments PRIMARY KEY(bundle_id,evaluation_id,criterion_id)
 );
END;
IF OBJECT_ID('dbo.result_revisions','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_revisions (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_result_revisions PRIMARY KEY,
  bundle_id BIGINT NOT NULL CONSTRAINT fk_revision_bundle REFERENCES dbo.result_correction_bundles(id),
  project_id BIGINT NOT NULL CONSTRAINT fk_revision_project REFERENCES dbo.projects(id),
  target_key BIGINT NOT NULL, student_id BIGINT NULL CONSTRAINT fk_revision_student REFERENCES dbo.users(id),
  version INT NOT NULL CONSTRAINT ck_result_revision_version CHECK(version >= 2),
  previous_revision_id BIGINT NULL CONSTRAINT fk_revision_previous REFERENCES dbo.result_revisions(id),
  snapshot_json NVARCHAR(MAX) NOT NULL CONSTRAINT ck_revision_snapshot CHECK(ISJSON(snapshot_json)=1),
  published_by BIGINT NOT NULL CONSTRAINT fk_revision_publisher REFERENCES dbo.users(id), published_at DATETIME2(0) NOT NULL,
  CONSTRAINT ck_revision_target CHECK((target_key=0 AND student_id IS NULL) OR (student_id IS NOT NULL AND target_key=student_id AND student_id>0)),
  CONSTRAINT ck_revision_chain CHECK((version=2 AND previous_revision_id IS NULL) OR (version>2 AND previous_revision_id IS NOT NULL)),
  CONSTRAINT uq_result_revision_version UNIQUE(project_id,target_key,version),
  CONSTRAINT uq_result_revision_bundle_target UNIQUE(bundle_id,target_key)
 );
END;
COMMIT;
GO
