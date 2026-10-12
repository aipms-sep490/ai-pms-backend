SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.result_appeal_policies','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_appeal_policies (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_appeal_policy PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_appeal_policy_project REFERENCES dbo.projects(id),
  version INT NOT NULL CONSTRAINT ck_appeal_policy_version CHECK(version>0),
  approval_reference NVARCHAR(1000) NOT NULL,
  recorded_by BIGINT NOT NULL CONSTRAINT fk_appeal_policy_recorder REFERENCES dbo.users(id),
  recorded_at DATETIME2(0) NOT NULL,
  CONSTRAINT uq_appeal_policy_version UNIQUE(project_id,version)
 );
END;
IF OBJECT_ID('dbo.result_appeal_windows','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_appeal_windows (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_appeal_window PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_appeal_window_project REFERENCES dbo.projects(id),
  policy_id BIGINT NOT NULL CONSTRAINT fk_appeal_window_policy REFERENCES dbo.result_appeal_policies(id),
  opens_at DATETIME2(0) NOT NULL, closes_at DATETIME2(0) NOT NULL,
  status VARCHAR(20) NOT NULL CONSTRAINT ck_appeal_window_status CHECK(status IN ('OPEN','CLOSED')),
  targets_json NVARCHAR(MAX) NOT NULL CONSTRAINT ck_appeal_window_targets CHECK(ISJSON(targets_json)=1),
  created_by BIGINT NOT NULL CONSTRAINT fk_appeal_window_creator REFERENCES dbo.users(id),
  concurrency_token UNIQUEIDENTIFIER NOT NULL,
  CONSTRAINT ck_appeal_window_dates CHECK(opens_at<closes_at)
 );
 CREATE INDEX ix_appeal_window_project ON dbo.result_appeal_windows(project_id,id DESC);
END;
IF OBJECT_ID('dbo.result_appeal_requests','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_appeal_requests (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_appeal_request PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_appeal_request_project REFERENCES dbo.projects(id),
  policy_id BIGINT NOT NULL CONSTRAINT fk_appeal_request_policy REFERENCES dbo.result_appeal_policies(id),
  window_id BIGINT NULL CONSTRAINT fk_appeal_request_window REFERENCES dbo.result_appeal_windows(id),
  source VARCHAR(30) NOT NULL CONSTRAINT ck_appeal_request_source CHECK(source IN ('STUDENT_APPEAL','STAFF_CORRECTION')),
  target_key BIGINT NOT NULL, student_id BIGINT NULL CONSTRAINT fk_appeal_request_student REFERENCES dbo.users(id),
  result_version INT NOT NULL CONSTRAINT ck_appeal_request_version CHECK(result_version>0),
  component_id BIGINT NOT NULL CONSTRAINT fk_appeal_request_component REFERENCES dbo.evaluation_scheme_components(id),
  scope VARCHAR(30) NOT NULL CONSTRAINT ck_appeal_request_scope CHECK(scope IN ('COMMON','MAJOR_SPECIFIC','INDIVIDUAL')),
  major_id BIGINT NULL CONSTRAINT fk_appeal_request_major REFERENCES dbo.majors(id),
  responsible_department_id BIGINT NOT NULL CONSTRAINT fk_appeal_request_department REFERENCES dbo.departments(id),
  frozen_inputs NVARCHAR(MAX) NOT NULL CONSTRAINT ck_appeal_request_snapshot CHECK(ISJSON(frozen_inputs)=1),
  base_stamp CHAR(64) NOT NULL,
  status VARCHAR(20) NOT NULL CONSTRAINT ck_appeal_request_status CHECK(status IN ('PENDING','UNDER_REVIEW','ACCEPTED','REJECTED')),
  requested_by BIGINT NOT NULL CONSTRAINT fk_appeal_request_creator REFERENCES dbo.users(id),
  requested_at DATETIME2(0) NOT NULL, reason NVARCHAR(2000) NOT NULL,
  reviewer_id BIGINT NULL CONSTRAINT fk_appeal_request_reviewer REFERENCES dbo.users(id),
  proposal VARCHAR(20) NULL, review_reason NVARCHAR(2000) NULL,
  bundle_id BIGINT NULL CONSTRAINT fk_appeal_request_bundle REFERENCES dbo.result_correction_bundles(id),
  decided_by BIGINT NULL CONSTRAINT fk_appeal_request_decider REFERENCES dbo.users(id),
  decided_at DATETIME2(0) NULL, decision_reason NVARCHAR(2000) NULL,
  concurrency_token UNIQUEIDENTIFIER NOT NULL,
  CONSTRAINT ck_appeal_request_target CHECK((target_key=0 AND student_id IS NULL) OR (student_id IS NOT NULL AND student_id>0 AND target_key=student_id)),
  CONSTRAINT ck_appeal_request_student CHECK(source<>'STUDENT_APPEAL' OR (student_id IS NOT NULL AND requested_by=student_id AND window_id IS NOT NULL)),
  CONSTRAINT ck_appeal_request_proposal CHECK(proposal IS NULL OR (proposal='KEEP' AND bundle_id IS NULL) OR (proposal='AMEND' AND bundle_id IS NOT NULL)),
  CONSTRAINT ck_appeal_request_accepted CHECK(status<>'ACCEPTED' OR (bundle_id IS NOT NULL AND decided_by IS NOT NULL AND decided_at IS NOT NULL))
  ,CONSTRAINT ck_appeal_independent_decision CHECK(decided_by IS NULL OR reviewer_id IS NULL OR decided_by<>reviewer_id)
 );
 CREATE UNIQUE INDEX ux_appeal_request_active ON dbo.result_appeal_requests(project_id,target_key,result_version) WHERE status IN ('PENDING','UNDER_REVIEW');
 CREATE INDEX ix_appeal_request_project ON dbo.result_appeal_requests(project_id,id DESC);
END;
IF OBJECT_ID('dbo.result_appeal_files','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_appeal_files (
  request_id BIGINT NOT NULL CONSTRAINT fk_appeal_file_request REFERENCES dbo.result_appeal_requests(id),
  file_id BIGINT NOT NULL CONSTRAINT fk_appeal_file_file REFERENCES dbo.files(id),
  file_name NVARCHAR(255) NOT NULL, content_type NVARCHAR(200) NOT NULL,
  size_bytes BIGINT NOT NULL CONSTRAINT ck_appeal_file_size CHECK(size_bytes>0 AND size_bytes<=10485760),
  sha256 CHAR(64) NOT NULL, storage_key NVARCHAR(500) NOT NULL,
  CONSTRAINT pk_appeal_file PRIMARY KEY(request_id,file_id)
 );
END;
IF OBJECT_ID('dbo.result_appeal_events','U') IS NULL
BEGIN
 CREATE TABLE dbo.result_appeal_events (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_appeal_event PRIMARY KEY,
  request_id BIGINT NOT NULL CONSTRAINT fk_appeal_event_request REFERENCES dbo.result_appeal_requests(id),
  actor_id BIGINT NOT NULL CONSTRAINT fk_appeal_event_actor REFERENCES dbo.users(id),
  bundle_id BIGINT NULL CONSTRAINT fk_appeal_event_bundle REFERENCES dbo.result_correction_bundles(id),
  action VARCHAR(40) NOT NULL, at DATETIME2(0) NOT NULL, reason NVARCHAR(2000) NULL
 );
 CREATE INDEX ix_appeal_event_request ON dbo.result_appeal_events(request_id,id DESC);
END;
COMMIT;
GO
