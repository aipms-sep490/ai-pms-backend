SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_copilot_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_copilot_snapshots
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_ai_copilot_snapshots PRIMARY KEY,
        feature_key varchar(48) NOT NULL,
        scope_type varchar(24) NOT NULL,
        scope_id bigint NOT NULL,
        project_id bigint NULL,
        department_id bigint NULL,
        status varchar(20) NOT NULL CONSTRAINT df_ai_copilot_snapshots_status DEFAULT ('COMPLETED'),
        model_version nvarchar(120) NULL,
        headline nvarchar(400) NOT NULL,
        payload_json nvarchar(max) NOT NULL,
        created_by bigint NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_ai_copilot_snapshots_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_ai_copilot_snapshots_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_ai_copilot_snapshots_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_ai_copilot_snapshots_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_ai_copilot_snapshots_status CHECK (status IN ('COMPLETED', 'FALLBACK')),
        CONSTRAINT ck_ai_copilot_snapshots_scope CHECK (scope_type IN ('PROJECT', 'TEAM', 'EVALUATION', 'APPEAL', 'DEPARTMENT', 'SEMESTER', 'GATE_INSTANCE', 'TASK', 'SPRINT'))
    );
    CREATE INDEX ix_ai_copilot_snapshots_lookup
        ON dbo.ai_copilot_snapshots(feature_key, scope_type, scope_id, created_at DESC, id DESC);
    CREATE INDEX ix_ai_copilot_snapshots_project
        ON dbo.ai_copilot_snapshots(project_id, created_at DESC)
        WHERE project_id IS NOT NULL;
END;

COMMIT TRANSACTION;
