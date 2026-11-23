SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- PR-P2-C: All-in-One Hub Pure Core (Spec Vault, Context Chat & Anchored Comments, Internal Heatmap, Health Pulse)
-- Zero AI features & zero external Git repo sync.

IF COL_LENGTH(N'dbo.deliverable_versions', N'changelog') IS NULL
    ALTER TABLE dbo.deliverable_versions ADD changelog nvarchar(2000) NULL;

IF COL_LENGTH(N'dbo.tasks', N'source_message_id') IS NULL
    ALTER TABLE dbo.tasks ADD source_message_id bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tasks') AND name = N'ux_tasks_source_message')
    EXEC(N'CREATE UNIQUE INDEX ux_tasks_source_message ON dbo.tasks(source_message_id) WHERE source_message_id IS NOT NULL AND deleted_at IS NULL');

IF COL_LENGTH(N'dbo.meeting_decisions', N'source_message_id') IS NULL
    ALTER TABLE dbo.meeting_decisions ADD source_message_id bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.meeting_decisions') AND name = N'ux_meeting_decisions_source_message')
    EXEC(N'CREATE UNIQUE INDEX ux_meeting_decisions_source_message ON dbo.meeting_decisions(source_message_id) WHERE source_message_id IS NOT NULL');

IF OBJECT_ID(N'dbo.chat_conversations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.chat_conversations', N'channel_scope') IS NULL
        EXEC(N'ALTER TABLE dbo.chat_conversations ADD channel_scope varchar(30) NULL');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_chat_conversations_channel_scope')
        EXEC(N'ALTER TABLE dbo.chat_conversations ADD CONSTRAINT ck_chat_conversations_channel_scope CHECK (channel_scope IS NULL OR channel_scope IN (''GENERAL'',''IT'',''MKT''))');
END;

IF OBJECT_ID(N'dbo.project_pinned_resources', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_pinned_resources (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_pinned_resources PRIMARY KEY,
        project_id bigint NOT NULL,
        resource_type varchar(30) NOT NULL CONSTRAINT df_project_pinned_resources_type DEFAULT 'OTHER',
        title nvarchar(200) NOT NULL,
        url nvarchar(1000) NOT NULL,
        created_by bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_project_pinned_resources_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT ck_project_pinned_resources_type CHECK (resource_type IN ('FIGMA','DRIVE','DOC','REPO_LINK','OTHER')),
        CONSTRAINT fk_project_pinned_resources_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_pinned_resources_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.evidence_anchored_comments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.evidence_anchored_comments (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_evidence_anchored_comments PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NULL,
        evidence_id bigint NULL,
        anchor_ref nvarchar(200) NOT NULL,
        severity varchar(20) NOT NULL CONSTRAINT df_evidence_anchored_comments_severity DEFAULT 'INFO',
        status varchar(20) NOT NULL CONSTRAINT df_evidence_anchored_comments_status DEFAULT 'OPEN',
        content nvarchar(2000) NOT NULL,
        created_by bigint NOT NULL,
        resolved_by bigint NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_evidence_anchored_comments_created_at DEFAULT SYSUTCDATETIME(),
        resolved_at datetime2(0) NULL,
        CONSTRAINT ck_evidence_anchored_comments_severity CHECK (severity IN ('INFO','WARNING','CRITICAL')),
        CONSTRAINT ck_evidence_anchored_comments_status CHECK (status IN ('OPEN','RESOLVED')),
        CONSTRAINT fk_evidence_anchored_comments_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_evidence_anchored_comments_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_evidence_anchored_comments_resolver FOREIGN KEY (resolved_by) REFERENCES dbo.users(id)
    );
    CREATE INDEX ix_evidence_anchored_comments_gate_status ON dbo.evidence_anchored_comments(project_id, gate_instance_id, status);
END;

IF OBJECT_ID(N'dbo.evidence_rubric_criterion_links', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.evidence_rubric_criterion_links (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_evidence_rubric_criterion_links PRIMARY KEY,
        project_id bigint NOT NULL,
        evidence_id bigint NOT NULL,
        rubric_criterion_id bigint NOT NULL,
        created_by bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_evidence_rubric_criterion_links_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_evidence_rubric_criterion_link UNIQUE (evidence_id, rubric_criterion_id),
        CONSTRAINT fk_evidence_rubric_links_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_evidence_rubric_links_criterion FOREIGN KEY (rubric_criterion_id) REFERENCES dbo.rubric_criteria(id),
        CONSTRAINT fk_evidence_rubric_links_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;

COMMIT TRANSACTION;
GO
