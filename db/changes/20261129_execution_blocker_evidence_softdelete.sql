SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.tasks', N'blocked_since_at') IS NULL
    ALTER TABLE dbo.tasks ADD blocked_since_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.tasks', N'blocker_reason') IS NULL
    ALTER TABLE dbo.tasks ADD blocker_reason nvarchar(1000) NULL;
IF COL_LENGTH(N'dbo.tasks', N'gate_requirement_id') IS NULL
    ALTER TABLE dbo.tasks ADD gate_requirement_id bigint NULL;
IF COL_LENGTH(N'dbo.tasks', N'deleted_at') IS NULL
    ALTER TABLE dbo.tasks ADD deleted_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.tasks', N'deleted_by') IS NULL
    ALTER TABLE dbo.tasks ADD deleted_by bigint NULL;
IF COL_LENGTH(N'dbo.tasks', N'restore_until_at') IS NULL
    ALTER TABLE dbo.tasks ADD restore_until_at datetime2(0) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_tasks_gate_requirement')
    EXEC(N'ALTER TABLE dbo.tasks ADD CONSTRAINT fk_tasks_gate_requirement FOREIGN KEY (gate_requirement_id) REFERENCES dbo.project_gate_requirements(id)');
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_tasks_deleted_by')
    EXEC(N'ALTER TABLE dbo.tasks ADD CONSTRAINT fk_tasks_deleted_by FOREIGN KEY (deleted_by) REFERENCES dbo.users(id)');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_tasks_active_milestone' AND object_id = OBJECT_ID(N'dbo.tasks'))
    EXEC(N'CREATE INDEX ix_tasks_active_milestone ON dbo.tasks(milestone_id) WHERE deleted_at IS NULL');

COMMIT TRANSACTION;
GO
