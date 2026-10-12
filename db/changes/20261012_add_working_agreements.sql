SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.team_members','membership_generation') IS NULL
 ALTER TABLE dbo.team_members ADD membership_generation UNIQUEIDENTIFIER NOT NULL
 CONSTRAINT df_team_member_generation DEFAULT(NEWID()) WITH VALUES;
IF OBJECT_ID('dbo.project_working_agreements','U') IS NULL
BEGIN
 CREATE TABLE dbo.project_working_agreements (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_working_agreements PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_working_agreement_project REFERENCES dbo.projects(id),
  version INT NOT NULL CONSTRAINT ck_working_agreement_version CHECK(version > 0),
  status VARCHAR(20) NOT NULL CONSTRAINT ck_working_agreement_status CHECK(status IN ('DRAFT','ACTIVE','SUPERSEDED')),
  content_json NVARCHAR(MAX) NOT NULL CONSTRAINT ck_working_agreement_content CHECK(ISJSON(content_json)=1),
  concurrency_token UNIQUEIDENTIFIER NOT NULL,
  created_by BIGINT NOT NULL CONSTRAINT fk_working_agreement_author REFERENCES dbo.users(id),
  created_at DATETIME2(0) NOT NULL, published_at DATETIME2(0) NULL,
  CONSTRAINT uq_working_agreement_version UNIQUE(project_id,version)
 );
 CREATE UNIQUE INDEX ux_working_agreement_active ON dbo.project_working_agreements(project_id) WHERE status='ACTIVE';
 CREATE UNIQUE INDEX ux_working_agreement_draft ON dbo.project_working_agreements(project_id) WHERE status='DRAFT';
END;
IF OBJECT_ID('dbo.agreement_members','U') IS NULL
BEGIN
 CREATE TABLE dbo.agreement_members (
  agreement_id BIGINT NOT NULL CONSTRAINT fk_agreement_member_agreement REFERENCES dbo.project_working_agreements(id),
  user_id BIGINT NOT NULL CONSTRAINT fk_agreement_member_user REFERENCES dbo.users(id),
  team_member_id BIGINT NOT NULL CONSTRAINT fk_agreement_member_membership REFERENCES dbo.team_members(id),
  membership_joined_at DATETIME2(0) NOT NULL,
  membership_generation UNIQUEIDENTIFIER NOT NULL,
  accepted_at DATETIME2(0) NULL,
  CONSTRAINT pk_agreement_members PRIMARY KEY(agreement_id,user_id)
 );
 CREATE INDEX ix_agreement_member_user ON dbo.agreement_members(user_id,agreement_id);
END;
COMMIT;
GO
