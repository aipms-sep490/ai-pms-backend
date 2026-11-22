SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.task_criteria', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_criteria
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_task_criteria PRIMARY KEY,
        task_id bigint NOT NULL,
        title nvarchar(500) NOT NULL,
        is_completed bit NOT NULL CONSTRAINT df_task_criteria_is_completed DEFAULT (0),
        sort_order int NOT NULL CONSTRAINT df_task_criteria_sort_order DEFAULT (0),
        completed_at datetime2(7) NULL,
        completed_by bigint NULL,
        created_by bigint NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_task_criteria_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at datetime2(7) NOT NULL CONSTRAINT df_task_criteria_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_task_criteria_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id) ON DELETE CASCADE,
        CONSTRAINT fk_task_criteria_completed_by FOREIGN KEY (completed_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_task_criteria_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_task_criteria_title CHECK (LEN(LTRIM(RTRIM(title))) > 0),
        CONSTRAINT ck_task_criteria_completion CHECK (
            (is_completed = 0 AND completed_at IS NULL AND completed_by IS NULL)
            OR (is_completed = 1 AND completed_at IS NOT NULL AND completed_by IS NOT NULL))
    );
    CREATE INDEX ix_task_criteria_task_order ON dbo.task_criteria(task_id, sort_order, id);
END;

IF OBJECT_ID(N'dbo.ai_setup_applications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ai_setup_applications
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_ai_setup_applications PRIMARY KEY,
        actor_id bigint NOT NULL,
        idempotency_key uniqueidentifier NOT NULL,
        scope_type varchar(24) NOT NULL,
        project_id bigint NULL,
        run_id bigint NULL,
        result_json nvarchar(max) NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_ai_setup_applications_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_ai_setup_applications_actor FOREIGN KEY (actor_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_ai_setup_applications_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT ck_ai_setup_applications_scope CHECK (scope_type IN ('PROJECT', 'RUBRIC')),
        CONSTRAINT uq_ai_setup_applications_actor_key UNIQUE (actor_id, idempotency_key)
    );
    CREATE INDEX ix_ai_setup_applications_project ON dbo.ai_setup_applications(project_id, created_at);
END;

COMMIT TRANSACTION;
