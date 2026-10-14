SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.industry_experts','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_experts (
  user_id BIGINT NOT NULL CONSTRAINT pk_industry_experts PRIMARY KEY CONSTRAINT fk_industry_expert_user REFERENCES dbo.users(id),
  organization NVARCHAR(255) NOT NULL,
  created_at DATETIME2(0) NOT NULL,
  created_by BIGINT NOT NULL CONSTRAINT fk_industry_expert_creator REFERENCES dbo.users(id),
  enrolled_at DATETIME2(0) NULL
 );
END;
IF OBJECT_ID('dbo.industry_invitations','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_invitations (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_industry_invitations PRIMARY KEY,
  expert_id BIGINT NOT NULL CONSTRAINT fk_industry_invite_expert REFERENCES dbo.industry_experts(user_id),
  email NVARCHAR(255) NOT NULL, token_hash VARCHAR(128) NOT NULL,
  protected_token NVARCHAR(MAX) NULL,
  created_at DATETIME2(0) NOT NULL, expires_at DATETIME2(0) NOT NULL,
  consumed_at DATETIME2(0) NULL, revoked_at DATETIME2(0) NULL,
  notification_id BIGINT NOT NULL CONSTRAINT fk_industry_invite_notification REFERENCES dbo.notifications(id),
  CONSTRAINT ck_industry_invite_dates CHECK(expires_at>created_at),
  CONSTRAINT uq_industry_invite_hash UNIQUE(token_hash),
  CONSTRAINT uq_industry_invite_notification UNIQUE(notification_id)
 );
 CREATE INDEX ix_industry_invite_expert ON dbo.industry_invitations(expert_id,id DESC);
END;
IF OBJECT_ID('dbo.industry_challenges','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_challenges (
  id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_industry_challenges PRIMARY KEY,
  nonce_hash VARCHAR(128) NOT NULL, browser_hash VARCHAR(128) NOT NULL,
  expires_at DATETIME2(0) NOT NULL, consumed_at DATETIME2(0) NULL
 );
 CREATE INDEX ix_industry_challenge_expiry ON dbo.industry_challenges(expires_at);
END;
IF OBJECT_ID('dbo.industry_assignments','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_assignments (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_industry_assignments PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_industry_assignment_project REFERENCES dbo.projects(id),
  expert_id BIGINT NOT NULL CONSTRAINT fk_industry_assignment_expert REFERENCES dbo.industry_experts(user_id),
  scope VARCHAR(20) NOT NULL, major_id BIGINT NULL CONSTRAINT fk_industry_assignment_major REFERENCES dbo.majors(id),
  starts_at DATETIME2(0) NOT NULL, ends_at DATETIME2(0) NOT NULL,
  assigned_by BIGINT NOT NULL CONSTRAINT fk_industry_assignment_creator REFERENCES dbo.users(id),
  created_at DATETIME2(0) NOT NULL, revoked_at DATETIME2(0) NULL,
  concurrency_token UNIQUEIDENTIFIER NOT NULL,
  CONSTRAINT ck_industry_assignment_scope CHECK((scope='COMMON' AND major_id IS NULL) OR (scope='MAJOR_SPECIFIC' AND major_id IS NOT NULL)),
  CONSTRAINT ck_industry_assignment_dates CHECK(ends_at>starts_at)
 );
 CREATE INDEX ix_industry_assignment_expert ON dbo.industry_assignments(expert_id,revoked_at,starts_at,ends_at);
 CREATE INDEX ix_industry_assignment_project ON dbo.industry_assignments(project_id,id DESC);
END;
IF OBJECT_ID('dbo.industry_evidence_shares','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_evidence_shares (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_industry_shares PRIMARY KEY,
  assignment_id BIGINT NOT NULL CONSTRAINT fk_industry_share_assignment REFERENCES dbo.industry_assignments(id),
  deliverable_version_id BIGINT NOT NULL CONSTRAINT fk_industry_share_version REFERENCES dbo.deliverable_versions(id),
  file_id BIGINT NOT NULL CONSTRAINT fk_industry_share_file REFERENCES dbo.files(id),
  file_name NVARCHAR(255) NOT NULL, content_type NVARCHAR(200) NOT NULL,
  size_bytes BIGINT NOT NULL CONSTRAINT ck_industry_share_size CHECK(size_bytes>0),
  sha256 CHAR(64) NOT NULL, storage_key NVARCHAR(500) NOT NULL,
  shared_by BIGINT NOT NULL CONSTRAINT fk_industry_share_creator REFERENCES dbo.users(id),
  shared_at DATETIME2(0) NOT NULL, revoked_at DATETIME2(0) NULL
 );
 CREATE UNIQUE INDEX ux_industry_share_active ON dbo.industry_evidence_shares(assignment_id,file_id) WHERE revoked_at IS NULL;
END;
IF OBJECT_ID('dbo.industry_feedback','U') IS NULL
BEGIN
 CREATE TABLE dbo.industry_feedback (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_industry_feedback PRIMARY KEY,
  assignment_id BIGINT NOT NULL CONSTRAINT fk_industry_feedback_assignment REFERENCES dbo.industry_assignments(id),
  version INT NOT NULL CONSTRAINT ck_industry_feedback_version CHECK(version>0),
  content NVARCHAR(MAX) NOT NULL CONSTRAINT ck_industry_feedback_length CHECK(LEN(content) BETWEEN 1 AND 10000),
  submitted_by BIGINT NOT NULL CONSTRAINT fk_industry_feedback_author REFERENCES dbo.users(id),
  submitted_at DATETIME2(0) NOT NULL,
  CONSTRAINT uq_industry_feedback_version UNIQUE(assignment_id,version)
 );
END;
COMMIT;
