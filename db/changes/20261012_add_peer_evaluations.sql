SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.peer_evaluation_rounds','U') IS NULL
BEGIN
 CREATE TABLE dbo.peer_evaluation_rounds (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_peer_rounds PRIMARY KEY,
  project_id BIGINT NOT NULL CONSTRAINT fk_peer_round_project REFERENCES dbo.projects(id),
  title NVARCHAR(200) NOT NULL, status VARCHAR(20) NOT NULL CONSTRAINT ck_peer_round_status CHECK(status IN ('OPEN','CLOSED')),
  opens_at DATETIME2(0) NOT NULL, closes_at DATETIME2(0) NOT NULL, closed_at DATETIME2(0) NULL,
  concurrency_token UNIQUEIDENTIFIER NOT NULL,
  created_by BIGINT NOT NULL CONSTRAINT fk_peer_round_author REFERENCES dbo.users(id),
  primary_assignment_id BIGINT NOT NULL CONSTRAINT fk_peer_round_primary REFERENCES dbo.supervisor_assignments(id),
  created_at DATETIME2(0) NOT NULL,
  CONSTRAINT ck_peer_round_dates CHECK(opens_at < closes_at),
  CONSTRAINT ck_peer_round_closed CHECK((status='OPEN' AND closed_at IS NULL) OR (status='CLOSED' AND closed_at IS NOT NULL))
 );
 CREATE UNIQUE INDEX ux_peer_round_open ON dbo.peer_evaluation_rounds(project_id) WHERE status='OPEN';
 CREATE INDEX ix_peer_round_project ON dbo.peer_evaluation_rounds(project_id,id DESC);
END;
IF OBJECT_ID('dbo.peer_round_members','U') IS NULL
BEGIN
 CREATE TABLE dbo.peer_round_members (
  round_id BIGINT NOT NULL CONSTRAINT fk_peer_member_round REFERENCES dbo.peer_evaluation_rounds(id),
  user_id BIGINT NOT NULL CONSTRAINT fk_peer_member_user REFERENCES dbo.users(id),
  major_id BIGINT NOT NULL CONSTRAINT fk_peer_member_major REFERENCES dbo.majors(id),
  department_id BIGINT NOT NULL CONSTRAINT fk_peer_member_department REFERENCES dbo.departments(id),
  team_member_id BIGINT NOT NULL CONSTRAINT fk_peer_member_membership REFERENCES dbo.team_members(id),
  membership_generation UNIQUEIDENTIFIER NOT NULL, membership_joined_at DATETIME2(0) NOT NULL,
  full_name NVARCHAR(255) NOT NULL,
  CONSTRAINT pk_peer_members PRIMARY KEY(round_id,user_id)
 );
END;
IF OBJECT_ID('dbo.peer_submissions','U') IS NULL
BEGIN
 CREATE TABLE dbo.peer_submissions (
  id BIGINT IDENTITY NOT NULL CONSTRAINT pk_peer_submissions PRIMARY KEY,
  round_id BIGINT NOT NULL CONSTRAINT fk_peer_submission_round REFERENCES dbo.peer_evaluation_rounds(id),
  author_id BIGINT NOT NULL CONSTRAINT fk_peer_submission_author REFERENCES dbo.users(id),
  status VARCHAR(20) NOT NULL CONSTRAINT ck_peer_submission_status CHECK(status IN ('DRAFT','SUBMITTED')),
  concurrency_token UNIQUEIDENTIFIER NOT NULL, updated_at DATETIME2(0) NOT NULL, submitted_at DATETIME2(0) NULL,
  CONSTRAINT uq_peer_submission_author UNIQUE(round_id,author_id),
  CONSTRAINT fk_peer_submission_roster FOREIGN KEY(round_id,author_id) REFERENCES dbo.peer_round_members(round_id,user_id),
  CONSTRAINT ck_peer_submission_time CHECK((status='DRAFT' AND submitted_at IS NULL) OR (status='SUBMITTED' AND submitted_at IS NOT NULL))
 );
END;
IF OBJECT_ID('dbo.peer_submission_items','U') IS NULL
BEGIN
 CREATE TABLE dbo.peer_submission_items (
  submission_id BIGINT NOT NULL CONSTRAINT fk_peer_item_submission REFERENCES dbo.peer_submissions(id),
  target_user_id BIGINT NOT NULL CONSTRAINT fk_peer_item_target REFERENCES dbo.users(id),
  rating INT NOT NULL CONSTRAINT ck_peer_rating CHECK(rating BETWEEN 1 AND 5),
  comment NVARCHAR(2000) NULL,
  CONSTRAINT pk_peer_items PRIMARY KEY(submission_id,target_user_id)
 );
END;
COMMIT;
GO
