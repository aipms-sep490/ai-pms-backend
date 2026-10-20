SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.review_gate_templates','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_templates(
  id bigint IDENTITY PRIMARY KEY, project_period_id bigint NULL, code varchar(50) NOT NULL, name nvarchar(255) NOT NULL,
  mode varchar(30) NOT NULL, major nvarchar(255) NOT NULL, status varchar(20) NOT NULL CONSTRAINT df_review_gate_template_status DEFAULT 'DRAFT',
  version int NOT NULL CONSTRAINT df_review_gate_template_version DEFAULT 1, concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_review_gate_template_token DEFAULT NEWID(),
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL CONSTRAINT df_review_gate_template_created DEFAULT SYSUTCDATETIME(), updated_at datetime2(0) NOT NULL CONSTRAINT df_review_gate_template_updated DEFAULT SYSUTCDATETIME(),
  CONSTRAINT uq_review_gate_template_code_version UNIQUE(code,version),
  CONSTRAINT ck_review_gate_template_mode CHECK(mode IN ('SINGLE_MAJOR','INTERDISCIPLINARY')),
  CONSTRAINT ck_review_gate_template_status CHECK(status IN ('DRAFT','PROPOSED','PUBLISHED')),
  CONSTRAINT fk_review_gate_template_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id),
  CONSTRAINT fk_review_gate_template_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id)
 );
END;
IF OBJECT_ID('dbo.review_gate_definitions','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_definitions(
  id bigint IDENTITY PRIMARY KEY, template_id bigint NOT NULL, code varchar(20) NOT NULL, sequence int NOT NULL,
  title nvarchar(255) NOT NULL, objective nvarchar(2000) NOT NULL, required bit NOT NULL CONSTRAINT df_review_gate_required DEFAULT 1,
  block_next_gate bit NOT NULL CONSTRAINT df_review_gate_block_next DEFAULT 1, blocks_defense bit NOT NULL CONSTRAINT df_review_gate_blocks_defense DEFAULT 0,
  window_start datetime2(0) NULL, window_end datetime2(0) NULL, reviewer_policy varchar(100) NOT NULL, review_scope varchar(30) NOT NULL,
  CONSTRAINT uq_review_gate_definition_code UNIQUE(template_id,code), CONSTRAINT fk_review_gate_definition_template FOREIGN KEY(template_id) REFERENCES dbo.review_gate_templates(id)
 );
END;
IF OBJECT_ID('dbo.review_gate_requirements','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_requirements(
  id bigint IDENTITY PRIMARY KEY, gate_definition_id bigint NOT NULL, code varchar(50) NOT NULL, title nvarchar(255) NOT NULL,
  description nvarchar(2000) NULL, type varchar(40) NOT NULL, scope varchar(30) NOT NULL, major_id bigint NULL,
  verification varchar(30) NOT NULL, required bit NOT NULL CONSTRAINT df_review_gate_requirement_required DEFAULT 1,
  CONSTRAINT uq_review_gate_requirement_code UNIQUE(gate_definition_id,code), CONSTRAINT fk_review_gate_requirement_gate FOREIGN KEY(gate_definition_id) REFERENCES dbo.review_gate_definitions(id),
  CONSTRAINT fk_review_gate_requirement_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id)
 );
END;
IF OBJECT_ID('dbo.project_gate_instances','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_gate_instances(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, template_id bigint NOT NULL, template_version int NOT NULL, code varchar(20) NOT NULL,
  sequence int NOT NULL, title nvarchar(255) NOT NULL, objective nvarchar(2000) NOT NULL, status varchar(30) NOT NULL,
  review_eligibility varchar(20) NOT NULL, required bit NOT NULL, window_start datetime2(0) NULL, window_end datetime2(0) NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_project_gate_token DEFAULT NEWID(), created_at datetime2(0) NOT NULL CONSTRAINT df_project_gate_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT uq_project_gate_code UNIQUE(project_id,code), CONSTRAINT fk_project_gate_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id), CONSTRAINT fk_project_gate_template FOREIGN KEY(template_id) REFERENCES dbo.review_gate_templates(id)
 );
END;
IF OBJECT_ID('dbo.project_gate_requirements','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_gate_requirements(
  id bigint IDENTITY PRIMARY KEY, gate_instance_id bigint NOT NULL, code varchar(50) NOT NULL, title nvarchar(255) NOT NULL, type varchar(40) NOT NULL,
  scope varchar(30) NOT NULL, major_id bigint NULL, required bit NOT NULL, verification_status varchar(20) NOT NULL,
  latest_evidence_id bigint NULL, reviewer_feedback nvarchar(2000) NULL,
  CONSTRAINT uq_project_gate_requirement_code UNIQUE(gate_instance_id,code), CONSTRAINT fk_project_gate_requirement_gate FOREIGN KEY(gate_instance_id) REFERENCES dbo.project_gate_instances(id), CONSTRAINT fk_project_gate_requirement_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id)
 );
END;
IF OBJECT_ID('dbo.review_gate_evidence','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_evidence(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, requirement_id bigint NOT NULL, version int NOT NULL, artifact_name nvarchar(255) NOT NULL,
  notes nvarchar(2000) NULL, submitted_by bigint NOT NULL, submitted_at datetime2(0) NOT NULL, status varchar(20) NOT NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_review_gate_evidence_token DEFAULT NEWID(),
  CONSTRAINT uq_review_gate_evidence_version UNIQUE(requirement_id,version), CONSTRAINT fk_review_gate_evidence_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id), CONSTRAINT fk_review_gate_evidence_requirement FOREIGN KEY(requirement_id) REFERENCES dbo.project_gate_requirements(id), CONSTRAINT fk_review_gate_evidence_submitter FOREIGN KEY(submitted_by) REFERENCES dbo.users(id)
 );
END;
IF COL_LENGTH('dbo.review_gate_evidence','content_type') IS NULL ALTER TABLE dbo.review_gate_evidence ADD content_type varchar(255) NOT NULL CONSTRAINT df_review_gate_evidence_content_type DEFAULT 'application/octet-stream';
IF COL_LENGTH('dbo.review_gate_evidence','size_bytes') IS NULL ALTER TABLE dbo.review_gate_evidence ADD size_bytes bigint NOT NULL CONSTRAINT df_review_gate_evidence_size DEFAULT 0;
IF COL_LENGTH('dbo.review_gate_evidence','sha256') IS NULL ALTER TABLE dbo.review_gate_evidence ADD sha256 varchar(64) NOT NULL CONSTRAINT df_review_gate_evidence_sha DEFAULT '';
IF COL_LENGTH('dbo.review_gate_evidence','storage_key') IS NULL ALTER TABLE dbo.review_gate_evidence ADD storage_key varchar(32) NOT NULL CONSTRAINT df_review_gate_evidence_storage DEFAULT '';
IF OBJECT_ID('dbo.review_gate_assignments','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_assignments(
  id bigint IDENTITY PRIMARY KEY, gate_instance_id bigint NOT NULL, reviewer_id bigint NOT NULL, scope varchar(30) NOT NULL, major_id bigint NULL,
  status varchar(20) NOT NULL, latest_decision varchar(30) NULL, reviewer_notes nvarchar(2000) NULL, concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_review_gate_assignment_token DEFAULT NEWID(), created_at datetime2(0) NOT NULL CONSTRAINT df_review_gate_assignment_created DEFAULT SYSUTCDATETIME(),
  CONSTRAINT uq_review_gate_assignment UNIQUE(gate_instance_id,reviewer_id,scope), CONSTRAINT fk_review_gate_assignment_gate FOREIGN KEY(gate_instance_id) REFERENCES dbo.project_gate_instances(id), CONSTRAINT fk_review_gate_assignment_reviewer FOREIGN KEY(reviewer_id) REFERENCES dbo.users(id), CONSTRAINT fk_review_gate_assignment_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id)
 );
END;
IF OBJECT_ID('dbo.review_gate_evidence_verifications','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_evidence_verifications(
  id bigint IDENTITY PRIMARY KEY, evidence_id bigint NOT NULL, assignment_id bigint NOT NULL, decision varchar(20) NOT NULL, feedback nvarchar(2000) NULL, reviewer_id bigint NOT NULL, created_at datetime2(0) NOT NULL,
  CONSTRAINT fk_review_gate_verification_evidence FOREIGN KEY(evidence_id) REFERENCES dbo.review_gate_evidence(id), CONSTRAINT fk_review_gate_verification_assignment FOREIGN KEY(assignment_id) REFERENCES dbo.review_gate_assignments(id), CONSTRAINT fk_review_gate_verification_reviewer FOREIGN KEY(reviewer_id) REFERENCES dbo.users(id)
 );
END;
IF OBJECT_ID('dbo.review_gate_decisions','U') IS NULL
BEGIN
 CREATE TABLE dbo.review_gate_decisions(
  id bigint IDENTITY PRIMARY KEY, assignment_id bigint NOT NULL, decision varchar(30) NOT NULL, notes nvarchar(2000) NOT NULL, decided_by bigint NOT NULL, decided_at datetime2(0) NOT NULL,
  CONSTRAINT fk_review_gate_decision_assignment FOREIGN KEY(assignment_id) REFERENCES dbo.review_gate_assignments(id), CONSTRAINT fk_review_gate_decision_actor FOREIGN KEY(decided_by) REFERENCES dbo.users(id)
 );
END;
IF OBJECT_ID('dbo.project_gate_scope_decisions','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_gate_scope_decisions(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, scope varchar(30) NOT NULL, major_id bigint NULL,
  status varchar(30) NOT NULL, notes nvarchar(2000) NULL, decided_by bigint NOT NULL, decided_at datetime2(0) NOT NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_project_gate_scope_token DEFAULT NEWID(),
  CONSTRAINT uq_project_gate_scope UNIQUE(project_id,scope),
  CONSTRAINT fk_project_gate_scope_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_project_gate_scope_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
  CONSTRAINT fk_project_gate_scope_actor FOREIGN KEY(decided_by) REFERENCES dbo.users(id)
 );
 CREATE INDEX ix_project_gate_scope_project ON dbo.project_gate_scope_decisions(project_id,status);
END;
COMMIT;
GO
