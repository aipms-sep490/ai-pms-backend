/* Additive migration for BE-03A: Reporting Cycles, Structured Progress Reports & Meetings, and Generic Project Action Items. Safe to run repeatedly. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Progress Report Periods (Reporting Cycles, Model P2)
IF OBJECT_ID(N'dbo.progress_report_periods', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.progress_report_periods (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_progress_report_periods PRIMARY KEY,
        project_id BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_project REFERENCES dbo.projects(id),
        project_period_id BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_period REFERENCES dbo.project_periods(id),
        report_type NVARCHAR(20) NOT NULL CONSTRAINT ck_progress_report_periods_type CHECK (report_type IN (N'WEEKLY', N'MONTHLY')),
        period_start DATETIME2(0) NOT NULL,
        period_end DATETIME2(0) NOT NULL,
        deadline DATETIME2(0) NOT NULL,
        late_policy NVARCHAR(20) NOT NULL CONSTRAINT df_progress_report_periods_late_policy DEFAULT (N'BLOCK') CONSTRAINT ck_progress_report_periods_policy CHECK (late_policy IN (N'BLOCK', N'FLAG')),
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_progress_report_periods_token DEFAULT (NEWSEQUENTIALID()),
        created_by BIGINT NOT NULL CONSTRAINT fk_progress_report_periods_creator REFERENCES dbo.users(id),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_progress_report_periods_range CHECK (period_end > period_start)
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_progress_report_periods_lookup' AND object_id = OBJECT_ID(N'dbo.progress_report_periods'))
    EXEC(N'CREATE INDEX ix_progress_report_periods_lookup ON dbo.progress_report_periods(project_id, report_type, period_start, period_end);');

-- 2. Alter dbo.progress_reports for reporting cycle reference, late tracking, and 5 structured sections
IF COL_LENGTH(N'dbo.progress_reports', N'progress_report_period_id') IS NULL
BEGIN
    ALTER TABLE dbo.progress_reports ADD progress_report_period_id BIGINT NULL;
    EXEC(N'ALTER TABLE dbo.progress_reports ADD CONSTRAINT fk_progress_reports_period_id FOREIGN KEY (progress_report_period_id) REFERENCES dbo.progress_report_periods(id);');
END;

IF COL_LENGTH(N'dbo.progress_reports', N'is_late') IS NULL
    ALTER TABLE dbo.progress_reports ADD is_late BIT NULL;

IF COL_LENGTH(N'dbo.progress_reports', N'in_progress_work') IS NULL
    ALTER TABLE dbo.progress_reports ADD in_progress_work NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.progress_reports', N'blockers') IS NULL
    ALTER TABLE dbo.progress_reports ADD blockers NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.progress_reports', N'risks') IS NULL
    ALTER TABLE dbo.progress_reports ADD risks NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.progress_reports', N'next_actions') IS NULL
    ALTER TABLE dbo.progress_reports ADD next_actions NVARCHAR(MAX) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'uq_progress_reports_period_id' AND object_id = OBJECT_ID(N'dbo.progress_reports'))
    EXEC(N'CREATE UNIQUE INDEX uq_progress_reports_period_id ON dbo.progress_reports(progress_report_period_id) WHERE progress_report_period_id IS NOT NULL;');

-- 3. Alter dbo.meetings for structured meeting content
IF COL_LENGTH(N'dbo.meetings', N'minutes') IS NULL
    ALTER TABLE dbo.meetings ADD minutes NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.meetings', N'decisions') IS NULL
    ALTER TABLE dbo.meetings ADD decisions NVARCHAR(MAX) NULL;

IF COL_LENGTH(N'dbo.meetings', N'blockers') IS NULL
    ALTER TABLE dbo.meetings ADD blockers NVARCHAR(MAX) NULL;

-- 4. Generic Project Action Items (Meeting XOR Progress Report)
IF OBJECT_ID(N'dbo.project_action_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_action_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_action_items PRIMARY KEY,
        project_id BIGINT NOT NULL CONSTRAINT fk_project_action_items_project REFERENCES dbo.projects(id),
        source_type NVARCHAR(30) NOT NULL,
        meeting_id BIGINT NULL CONSTRAINT fk_project_action_items_meeting REFERENCES dbo.meetings(id),
        progress_report_id BIGINT NULL CONSTRAINT fk_project_action_items_report REFERENCES dbo.progress_reports(id),
        title NVARCHAR(500) NOT NULL,
        description NVARCHAR(MAX) NULL,
        owner_id BIGINT NULL CONSTRAINT fk_project_action_items_owner REFERENCES dbo.users(id),
        task_id BIGINT NULL CONSTRAINT fk_project_action_items_task REFERENCES dbo.tasks(id),
        milestone_id BIGINT NULL CONSTRAINT fk_project_action_items_milestone REFERENCES dbo.milestones(id),
        due_at DATETIME2(0) NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_project_action_items_status DEFAULT (N'TODO'),
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_project_action_items_token DEFAULT (NEWSEQUENTIALID()),
        created_by BIGINT NOT NULL CONSTRAINT fk_project_action_items_creator REFERENCES dbo.users(id),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_project_action_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_project_action_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_project_action_items_source CHECK (
            (source_type = N'MEETING' AND meeting_id IS NOT NULL AND progress_report_id IS NULL) OR
            (source_type = N'PROGRESS_REPORT' AND progress_report_id IS NOT NULL AND meeting_id IS NULL)
        ),
        CONSTRAINT ck_project_action_items_status CHECK (status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'DONE', N'CANCELLED'))
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_project_action_items_project' AND object_id = OBJECT_ID(N'dbo.project_action_items'))
    EXEC(N'CREATE INDEX ix_project_action_items_project ON dbo.project_action_items(project_id, status, due_at);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_project_action_items_owner' AND object_id = OBJECT_ID(N'dbo.project_action_items'))
    EXEC(N'CREATE INDEX ix_project_action_items_owner ON dbo.project_action_items(owner_id, status);');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_project_action_items_meeting' AND object_id = OBJECT_ID(N'dbo.project_action_items'))
    EXEC(N'CREATE INDEX ix_project_action_items_meeting ON dbo.project_action_items(meeting_id) WHERE meeting_id IS NOT NULL;');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_project_action_items_report' AND object_id = OBJECT_ID(N'dbo.project_action_items'))
    EXEC(N'CREATE INDEX ix_project_action_items_report ON dbo.project_action_items(progress_report_id) WHERE progress_report_id IS NOT NULL;');

COMMIT TRANSACTION;
