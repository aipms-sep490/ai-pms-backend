SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.project_evidence_reviews','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_evidence_reviews(
  id bigint IDENTITY PRIMARY KEY, evidence_id bigint NOT NULL, source_version_id bigint NULL,
  source_version varchar(64) NOT NULL, source_snapshot_json nvarchar(max) NOT NULL,
  reviewer_id bigint NOT NULL, supervisor_assignment_id bigint NOT NULL,
  decision varchar(20) NOT NULL, reason nvarchar(2000) NOT NULL, reviewed_at datetime2(0) NOT NULL,
  CONSTRAINT fk_evidence_review_evidence FOREIGN KEY(evidence_id) REFERENCES dbo.project_evidence(id),
  CONSTRAINT fk_evidence_review_version FOREIGN KEY(source_version_id) REFERENCES dbo.deliverable_versions(id),
  CONSTRAINT fk_evidence_review_reviewer FOREIGN KEY(reviewer_id) REFERENCES dbo.users(id),
  CONSTRAINT fk_evidence_review_assignment FOREIGN KEY(supervisor_assignment_id) REFERENCES dbo.supervisor_assignments(id),
  CONSTRAINT ck_evidence_review_json CHECK(ISJSON(source_snapshot_json)=1),
  CONSTRAINT ck_evidence_review_decision CHECK(decision IN ('ACCEPTED','CHANGES_REQUESTED','REJECTED')),
  CONSTRAINT ck_evidence_review_reason CHECK(LEN(LTRIM(RTRIM(reason)))>0));
 CREATE INDEX ix_evidence_review_history ON dbo.project_evidence_reviews(evidence_id,id);
END;
IF OBJECT_ID('dbo.project_checkpoints','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_checkpoints(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, title nvarchar(200) NOT NULL, major_id bigint NULL,
  planned_at datetime2(0) NOT NULL, concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_checkpoint_token DEFAULT NEWID(),
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL, updated_at datetime2(0) NOT NULL,
  CONSTRAINT fk_checkpoint_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_checkpoint_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
  CONSTRAINT fk_checkpoint_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id));
 CREATE INDEX ix_checkpoint_project ON dbo.project_checkpoints(project_id,major_id,planned_at,id);
END;
IF OBJECT_ID('dbo.project_checkpoint_decisions','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_checkpoint_decisions(
  id bigint IDENTITY PRIMARY KEY, checkpoint_id bigint NOT NULL, decision varchar(20) NOT NULL, reason nvarchar(2000) NOT NULL,
  evidence_json nvarchar(max) NOT NULL, decided_by bigint NOT NULL, decided_at datetime2(0) NOT NULL,
  CONSTRAINT fk_checkpoint_decision_checkpoint FOREIGN KEY(checkpoint_id) REFERENCES dbo.project_checkpoints(id),
  CONSTRAINT fk_checkpoint_decision_actor FOREIGN KEY(decided_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_checkpoint_decision CHECK(decision IN ('MET','AT_RISK','NOT_MET','CANCELLED')),
  CONSTRAINT ck_checkpoint_evidence CHECK(ISJSON(evidence_json)=1),
  CONSTRAINT ck_checkpoint_reason CHECK(LEN(LTRIM(RTRIM(reason)))>0));
 CREATE INDEX ix_checkpoint_decisions ON dbo.project_checkpoint_decisions(checkpoint_id,id);
END;
IF OBJECT_ID('dbo.defense_committees','U') IS NULL
BEGIN
 CREATE TABLE dbo.defense_committees(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, name nvarchar(200) NOT NULL, created_by bigint NOT NULL, created_at datetime2(0) NOT NULL,
  CONSTRAINT fk_defense_committee_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_defense_committee_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id));
 CREATE INDEX ix_defense_committee_project ON dbo.defense_committees(project_id,id);
END;
IF OBJECT_ID('dbo.defense_committee_members','U') IS NULL
 CREATE TABLE dbo.defense_committee_members(
  committee_id bigint NOT NULL, user_id bigint NOT NULL, major_id bigint NOT NULL,
  CONSTRAINT pk_defense_members PRIMARY KEY(committee_id,user_id,major_id),
  CONSTRAINT fk_defense_member_committee FOREIGN KEY(committee_id) REFERENCES dbo.defense_committees(id),
  CONSTRAINT fk_defense_member_user FOREIGN KEY(user_id) REFERENCES dbo.users(id),
  CONSTRAINT fk_defense_member_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id));
IF OBJECT_ID('dbo.defense_sessions','U') IS NULL
BEGIN
 CREATE TABLE dbo.defense_sessions(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, committee_id bigint NOT NULL,
  start_at datetime2(0) NOT NULL, end_at datetime2(0) NOT NULL, room nvarchar(100) NOT NULL, status varchar(20) NOT NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_defense_token DEFAULT NEWID(),
  created_by bigint NOT NULL, created_at datetime2(0) NOT NULL,
  cancellation_reason nvarchar(2000) NULL, cancelled_by bigint NULL, cancelled_at datetime2(0) NULL,
  CONSTRAINT fk_defense_session_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_defense_session_committee FOREIGN KEY(committee_id) REFERENCES dbo.defense_committees(id),
  CONSTRAINT fk_defense_session_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT fk_defense_session_canceller FOREIGN KEY(cancelled_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_defense_dates CHECK(end_at>start_at AND DATEDIFF(second,start_at,end_at)<=28800),
  CONSTRAINT ck_defense_status CHECK((status='SCHEDULED' AND cancelled_at IS NULL AND cancelled_by IS NULL AND cancellation_reason IS NULL)
   OR (status='CANCELLED' AND cancelled_at IS NOT NULL AND cancelled_by IS NOT NULL AND cancellation_reason IS NOT NULL AND LEN(LTRIM(RTRIM(cancellation_reason)))>0)));
 CREATE UNIQUE INDEX ux_defense_scheduled_project ON dbo.defense_sessions(project_id) WHERE status='SCHEDULED';
 CREATE INDEX ix_defense_schedule ON dbo.defense_sessions(status,start_at,end_at) INCLUDE(project_id,committee_id,room);
END;
COMMIT;
GO
