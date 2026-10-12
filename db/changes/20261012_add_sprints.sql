SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.sprints','U') IS NULL
BEGIN
 CREATE TABLE dbo.sprints(
  id bigint IDENTITY PRIMARY KEY, project_id bigint NOT NULL, name nvarchar(200) NOT NULL,
  status varchar(16) NOT NULL CONSTRAINT df_sprint_status DEFAULT 'PLANNED',
  start_at datetime2(7) NOT NULL,end_at datetime2(7) NOT NULL,started_at datetime2(7) NULL,completed_at datetime2(7) NULL,
  concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_sprint_token DEFAULT NEWID(),
  created_by bigint NOT NULL,created_at datetime2(7) NOT NULL,updated_at datetime2(7) NOT NULL,
  CONSTRAINT fk_sprint_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
  CONSTRAINT fk_sprint_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_sprint_status CHECK(status IN ('PLANNED','ACTIVE','COMPLETED')),
  CONSTRAINT ck_sprint_dates CHECK(end_at>start_at AND DATEDIFF(day,start_at,end_at)<=90),
  CONSTRAINT ck_sprint_lifecycle CHECK((status='PLANNED' AND started_at IS NULL AND completed_at IS NULL)
    OR(status='ACTIVE' AND started_at IS NOT NULL AND completed_at IS NULL)
    OR(status='COMPLETED' AND started_at IS NOT NULL AND completed_at IS NOT NULL AND completed_at>=started_at)));
 CREATE UNIQUE INDEX ux_sprint_active ON dbo.sprints(project_id) WHERE status='ACTIVE';
 CREATE INDEX ix_sprint_project ON dbo.sprints(project_id,id);
END;
IF COL_LENGTH('dbo.tasks','sprint_id') IS NULL ALTER TABLE dbo.tasks ADD sprint_id bigint NULL;
IF COL_LENGTH('dbo.tasks','story_points') IS NULL ALTER TABLE dbo.tasks ADD story_points int NULL;
IF COL_LENGTH('dbo.tasks','task_code') IS NULL EXEC(N'ALTER TABLE dbo.tasks ADD task_code AS (''TASK-''+CONVERT(varchar(20),id)) PERSISTED');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='fk_task_sprint')
 EXEC(N'ALTER TABLE dbo.tasks ADD CONSTRAINT fk_task_sprint FOREIGN KEY(sprint_id) REFERENCES dbo.sprints(id)');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_task_story_points')
 EXEC(N'ALTER TABLE dbo.tasks ADD CONSTRAINT ck_task_story_points CHECK(story_points IS NULL OR story_points IN (1,2,3,5,8,13))');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tasks') AND name='ux_task_code')
 EXEC(N'CREATE UNIQUE INDEX ux_task_code ON dbo.tasks(task_code)');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.tasks') AND name='ix_task_sprint')
 EXEC(N'CREATE INDEX ix_task_sprint ON dbo.tasks(sprint_id,id)');
IF OBJECT_ID('dbo.task_board_labels','U') IS NULL
BEGIN
 CREATE TABLE dbo.task_board_labels(task_id bigint NOT NULL,label varchar(32) NOT NULL,
  CONSTRAINT pk_task_board_labels PRIMARY KEY(task_id,label),
  CONSTRAINT fk_board_label_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id) ON DELETE CASCADE);
 CREATE INDEX ix_board_label ON dbo.task_board_labels(label,task_id);
END;
IF OBJECT_ID('dbo.sprint_task_events','U') IS NULL
BEGIN
 CREATE TABLE dbo.sprint_task_events(id bigint IDENTITY PRIMARY KEY,sprint_id bigint NOT NULL,task_id bigint NOT NULL,
  kind varchar(16) NOT NULL,in_scope bit NOT NULL,task_status varchar(20) NOT NULL,story_points int NULL,
  actor_id bigint NOT NULL,occurred_at datetime2(7) NOT NULL,
  CONSTRAINT fk_sprint_event_sprint FOREIGN KEY(sprint_id) REFERENCES dbo.sprints(id),
  CONSTRAINT fk_sprint_event_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id),
  CONSTRAINT fk_sprint_event_actor FOREIGN KEY(actor_id) REFERENCES dbo.users(id),
  CONSTRAINT ck_sprint_event_kind CHECK(kind IN ('BASELINE','SCOPE','ESTIMATE','STATUS')),
  CONSTRAINT ck_sprint_event_points CHECK(story_points IS NULL OR story_points IN (1,2,3,5,8,13)));
 CREATE INDEX ix_sprint_events ON dbo.sprint_task_events(sprint_id,id);
END;
COMMIT;
GO
