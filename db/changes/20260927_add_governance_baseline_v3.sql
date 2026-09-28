/* Additive governance foundation for the v3 baseline. Safe to run repeatedly. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.tasks', N'concurrency_token') IS NULL
    ALTER TABLE dbo.tasks ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_tasks_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.milestones', N'concurrency_token') IS NULL
    ALTER TABLE dbo.milestones ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_milestones_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.progress_reports', N'concurrency_token') IS NULL
    ALTER TABLE dbo.progress_reports ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_progress_reports_concurrency_token DEFAULT (NEWSEQUENTIALID());
IF COL_LENGTH(N'dbo.meetings', N'concurrency_token') IS NULL
    ALTER TABLE dbo.meetings ADD concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meetings_concurrency_token DEFAULT (NEWSEQUENTIALID());

IF OBJECT_ID(N'dbo.meeting_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_decisions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_decisions PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        content NVARCHAR(4000) NOT NULL,
        decided_by BIGINT NOT NULL,
        decided_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_decided_at DEFAULT (SYSUTCDATETIME()),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_decisions_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_meeting_decisions_user FOREIGN KEY (decided_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.meeting_action_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_action_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_action_items PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        title NVARCHAR(500) NOT NULL,
        description NVARCHAR(4000) NULL,
        assignee_user_id BIGINT NULL,
        due_at DATETIME2(0) NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_meeting_action_items_status DEFAULT (N'OPEN'),
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meeting_action_items_token DEFAULT (NEWSEQUENTIALID()),
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_meeting_action_items_status CHECK (status IN (N'OPEN',N'IN_PROGRESS',N'DONE',N'CANCELLED')),
        CONSTRAINT fk_meeting_action_items_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_meeting_action_items_assignee FOREIGN KEY (assignee_user_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_meeting_action_items_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_decisions_meeting' AND object_id = OBJECT_ID(N'dbo.meeting_decisions'))
    CREATE INDEX ix_meeting_decisions_meeting ON dbo.meeting_decisions(meeting_id, decided_at DESC);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_action_items_meeting' AND object_id = OBJECT_ID(N'dbo.meeting_action_items'))
    CREATE INDEX ix_meeting_action_items_meeting ON dbo.meeting_action_items(meeting_id, status, due_at);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_meeting_action_items_assignee' AND object_id = OBJECT_ID(N'dbo.meeting_action_items'))
    CREATE INDEX ix_meeting_action_items_assignee ON dbo.meeting_action_items(assignee_user_id, status);
COMMIT TRANSACTION;
