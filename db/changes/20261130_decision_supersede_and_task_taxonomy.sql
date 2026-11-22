SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Decision Supersession (append-only chain on dbo.meeting_decisions)
IF COL_LENGTH(N'dbo.meeting_decisions', N'superseded_by_decision_id') IS NULL
    ALTER TABLE dbo.meeting_decisions ADD superseded_by_decision_id bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_meeting_decisions_superseded_by')
    EXEC(N'ALTER TABLE dbo.meeting_decisions ADD CONSTRAINT fk_meeting_decisions_superseded_by FOREIGN KEY (superseded_by_decision_id) REFERENCES dbo.meeting_decisions(id)');

-- 2. Task Taxonomy (9 IT / 8 MKT / 5 Integration), Discipline Sub-status, and IT<->MKT Handoff flag
IF COL_LENGTH(N'dbo.tasks', N'task_category') IS NULL
    ALTER TABLE dbo.tasks ADD task_category varchar(60) NULL;
IF COL_LENGTH(N'dbo.tasks', N'discipline_sub_status') IS NULL
    ALTER TABLE dbo.tasks ADD discipline_sub_status varchar(40) NULL;
IF COL_LENGTH(N'dbo.tasks', N'handoff_flag') IS NULL
    ALTER TABLE dbo.tasks ADD handoff_flag varchar(30) NULL;
IF COL_LENGTH(N'dbo.tasks', N'handoff_flagged_at') IS NULL
    ALTER TABLE dbo.tasks ADD handoff_flagged_at datetime2(0) NULL;

-- 3. Project Labels catalog (custom colored labels with descriptions; backward compatible with task_board_labels)
IF OBJECT_ID(N'dbo.project_labels', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_labels (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_labels PRIMARY KEY,
        project_id bigint NOT NULL,
        slug varchar(32) NOT NULL,
        name nvarchar(60) NOT NULL,
        color_hex char(7) NOT NULL,
        description nvarchar(200) NULL,
        is_system bit NOT NULL CONSTRAINT df_project_labels_is_system DEFAULT 0,
        created_by bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_project_labels_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_project_labels_slug UNIQUE (project_id, slug),
        CONSTRAINT fk_project_labels_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_labels_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

COMMIT TRANSACTION;
GO
