SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.files', N'task_id') IS NULL ALTER TABLE dbo.files ADD task_id BIGINT NULL;
GO
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_files_single_parent')
    ALTER TABLE dbo.files DROP CONSTRAINT ck_files_single_parent;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_files_single_parent')
    ALTER TABLE dbo.files ADD CONSTRAINT ck_files_single_parent CHECK (
        (CASE WHEN deliverable_version_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN progress_report_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN meeting_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN supervisor_feedback_id IS NULL THEN 0 ELSE 1 END) +
        (CASE WHEN task_id IS NULL THEN 0 ELSE 1 END) <= 1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_files_task')
    ALTER TABLE dbo.files ADD CONSTRAINT fk_files_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_files_task_id' AND object_id = OBJECT_ID(N'dbo.files'))
    CREATE INDEX ix_files_task_id ON dbo.files(task_id) WHERE task_id IS NOT NULL;
IF OBJECT_ID(N'dbo.task_comments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_comments (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_task_comments PRIMARY KEY,
        task_id BIGINT NOT NULL,
        author_id BIGINT NOT NULL,
        content NVARCHAR(4000) NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_task_comments_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_task_comments_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_task_comments_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_task_comments_author FOREIGN KEY (author_id) REFERENCES dbo.users(id)
    );
    CREATE INDEX ix_task_comments_task_created ON dbo.task_comments(task_id, created_at, id);
END;
COMMIT TRANSACTION;
