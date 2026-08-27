SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.progress_report_periods', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.progress_report_periods (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_progress_report_periods PRIMARY KEY,
        project_id BIGINT NOT NULL,
        report_type NVARCHAR(20) NOT NULL,
        period_start DATE NOT NULL,
        period_end DATE NOT NULL,
        deadline_at DATETIME2(0) NOT NULL,
        late_policy NVARCHAR(10) NOT NULL CONSTRAINT df_progress_report_periods_late_policy DEFAULT (N'FLAG'),
        status NVARCHAR(20) NOT NULL CONSTRAINT df_progress_report_periods_status DEFAULT (N'OPEN'),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_periods_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_progress_report_periods_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT uq_progress_report_periods UNIQUE (project_id, report_type, period_start, period_end),
        CONSTRAINT ck_progress_report_periods_type CHECK (report_type IN (N'WEEKLY', N'MONTHLY')),
        CONSTRAINT ck_progress_report_periods_policy CHECK (late_policy IN (N'BLOCK', N'FLAG')),
        CONSTRAINT ck_progress_report_periods_status CHECK (status IN (N'OPEN', N'CLOSED')),
        CONSTRAINT ck_progress_report_periods_dates CHECK (period_end >= period_start)
    );
    CREATE INDEX ix_progress_report_periods_project_deadline ON dbo.progress_report_periods(project_id, deadline_at);
END;

IF OBJECT_ID(N'dbo.progress_report_metadata', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.progress_report_metadata (
        report_id BIGINT NOT NULL CONSTRAINT pk_progress_report_metadata PRIMARY KEY,
        report_period_id BIGINT NOT NULL,
        is_late BIT NOT NULL CONSTRAINT df_progress_report_metadata_is_late DEFAULT (0),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_metadata_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_metadata_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_progress_report_metadata_report FOREIGN KEY (report_id) REFERENCES dbo.progress_reports(id) ON DELETE CASCADE,
        CONSTRAINT fk_progress_report_metadata_period FOREIGN KEY (report_period_id) REFERENCES dbo.progress_report_periods(id),
        CONSTRAINT uq_progress_report_metadata_period UNIQUE (report_period_id)
    );
END;

IF OBJECT_ID(N'dbo.progress_report_sections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.progress_report_sections (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_progress_report_sections PRIMARY KEY,
        report_id BIGINT NOT NULL,
        section_type NVARCHAR(30) NOT NULL,
        content NVARCHAR(MAX) NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_sections_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_sections_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_progress_report_sections_report FOREIGN KEY (report_id) REFERENCES dbo.progress_reports(id) ON DELETE CASCADE,
        CONSTRAINT uq_progress_report_sections UNIQUE (report_id, section_type),
        CONSTRAINT ck_progress_report_sections_type CHECK (section_type IN (N'COMPLETED', N'IN_PROGRESS', N'BLOCKERS', N'RISKS', N'NEXT_ACTIONS'))
    );
END;

IF OBJECT_ID(N'dbo.progress_report_contributions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.progress_report_contributions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_progress_report_contributions PRIMARY KEY,
        report_id BIGINT NOT NULL,
        contributor_id BIGINT NOT NULL,
        section_type NVARCHAR(30) NOT NULL,
        content NVARCHAR(MAX) NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_contributions_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_progress_report_contributions_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_progress_report_contributions_report FOREIGN KEY (report_id) REFERENCES dbo.progress_reports(id) ON DELETE CASCADE,
        CONSTRAINT fk_progress_report_contributions_user FOREIGN KEY (contributor_id) REFERENCES dbo.users(id),
        CONSTRAINT ck_progress_report_contributions_type CHECK (section_type IN (N'COMPLETED', N'IN_PROGRESS', N'BLOCKERS', N'RISKS', N'NEXT_ACTIONS'))
    );
END;

IF OBJECT_ID(N'dbo.meeting_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_decisions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_decisions PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        content NVARCHAR(MAX) NOT NULL,
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_decisions_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_decisions_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_decisions_user FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.meeting_blockers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_blockers (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_blockers PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        content NVARCHAR(MAX) NOT NULL,
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_blockers_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_blockers_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_blockers_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_blockers_user FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.meeting_action_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_action_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_action_items PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        title NVARCHAR(255) NOT NULL,
        description NVARCHAR(MAX) NULL,
        owner_user_id BIGINT NOT NULL,
        due_date DATE NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_meeting_action_items_status DEFAULT (N'TODO'),
        task_id BIGINT NULL,
        milestone_id BIGINT NULL,
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_meeting_action_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_action_items_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_action_items_owner FOREIGN KEY (owner_user_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_meeting_action_items_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_meeting_action_items_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_meeting_action_items_milestone FOREIGN KEY (milestone_id) REFERENCES dbo.milestones(id),
        CONSTRAINT ck_meeting_action_items_status CHECK (status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'DONE', N'CANCELLED'))
    );
    CREATE INDEX ix_meeting_action_items_owner_status ON dbo.meeting_action_items(owner_user_id, status);
END;

IF OBJECT_ID(N'dbo.meeting_action_items', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.ck_meeting_action_items_status', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.meeting_action_items DROP CONSTRAINT ck_meeting_action_items_status;
    ALTER TABLE dbo.meeting_action_items WITH CHECK ADD CONSTRAINT ck_meeting_action_items_status
        CHECK (status IN (N'TODO', N'IN_PROGRESS', N'BLOCKED', N'DONE', N'CANCELLED'));
END;

COMMIT TRANSACTION;
