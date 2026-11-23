SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. AcademicBlueprint (single source of truth per semester + track; dual-department co-authoring & sign-off for IT_MKT)
IF OBJECT_ID(N'dbo.academic_blueprints', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_blueprints (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_blueprints PRIMARY KEY,
        academic_semester_id bigint NOT NULL,
        track_code varchar(20) NOT NULL,
        code varchar(60) NOT NULL,
        name nvarchar(255) NOT NULL,
        version int NOT NULL CONSTRAINT df_academic_blueprints_version DEFAULT 1,
        status varchar(20) NOT NULL CONSTRAINT df_academic_blueprints_status DEFAULT 'DRAFT',
        lead_department_id bigint NOT NULL,
        participating_department_id bigint NULL,
        period_policy_version_id bigint NULL,
        gate_template_id bigint NULL,
        milestone_template_id bigint NULL,
        rubric_id bigint NULL,
        config_json nvarchar(max) NOT NULL CONSTRAINT df_academic_blueprints_config DEFAULT N'{}',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_academic_blueprints_token DEFAULT NEWID(),
        created_by bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_academic_blueprints_created_at DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_academic_blueprints_updated_at DEFAULT SYSUTCDATETIME(),
        published_at datetime2(0) NULL,
        CONSTRAINT uq_academic_blueprints_code_ver UNIQUE (academic_semester_id, code, version),
        CONSTRAINT fk_academic_blueprints_semester FOREIGN KEY (academic_semester_id) REFERENCES dbo.academic_semesters(id),
        CONSTRAINT fk_academic_blueprints_lead_dept FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_academic_blueprints_part_dept FOREIGN KEY (participating_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_academic_blueprints_gate_tpl FOREIGN KEY (gate_template_id) REFERENCES dbo.review_gate_templates(id),
        CONSTRAINT fk_academic_blueprints_ms_tpl FOREIGN KEY (milestone_template_id) REFERENCES dbo.milestone_templates(id),
        CONSTRAINT fk_academic_blueprints_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
        CONSTRAINT fk_academic_blueprints_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_academic_blueprints_track CHECK (track_code IN ('IT_ONLY','MKT_ONLY','IT_MKT')),
        CONSTRAINT ck_academic_blueprints_status CHECK (status IN ('DRAFT','IN_REVIEW','PUBLISHED','RETIRED')),
        CONSTRAINT ck_academic_blueprints_version CHECK (version > 0)
    );
    CREATE INDEX ix_academic_blueprints_semester_track ON dbo.academic_blueprints(academic_semester_id, track_code, status);
END;

IF OBJECT_ID(N'dbo.academic_blueprint_approvals', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_blueprint_approvals (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_blueprint_approvals PRIMARY KEY,
        blueprint_id bigint NOT NULL,
        department_id bigint NOT NULL,
        scope_role varchar(30) NOT NULL,
        decision varchar(20) NOT NULL CONSTRAINT df_blueprint_approvals_decision DEFAULT 'PENDING',
        decided_by bigint NULL,
        decided_at datetime2(0) NULL,
        notes nvarchar(1000) NULL,
        CONSTRAINT uq_academic_blueprint_approvals UNIQUE (blueprint_id, department_id),
        CONSTRAINT fk_blueprint_approvals_bp FOREIGN KEY (blueprint_id) REFERENCES dbo.academic_blueprints(id),
        CONSTRAINT fk_blueprint_approvals_dept FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_blueprint_approvals_actor FOREIGN KEY (decided_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_blueprint_approvals_role CHECK (scope_role IN ('LEAD','PARTICIPATING')),
        CONSTRAINT ck_blueprint_approvals_decision CHECK (decision IN ('PENDING','APPROVED','REJECTED'))
    );
END;

IF OBJECT_ID(N'dbo.project_blueprint_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_blueprint_snapshots (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_blueprint_snapshots PRIMARY KEY,
        project_id bigint NOT NULL,
        blueprint_id bigint NOT NULL,
        blueprint_version int NOT NULL,
        snapshot_json nvarchar(max) NOT NULL,
        applied_by bigint NOT NULL,
        applied_at datetime2(0) NOT NULL CONSTRAINT df_project_blueprint_snapshots_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_project_blueprint_snapshots_project UNIQUE (project_id),
        CONSTRAINT fk_project_bp_snap_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_bp_snap_bp FOREIGN KEY (blueprint_id) REFERENCES dbo.academic_blueprints(id),
        CONSTRAINT fk_project_bp_snap_actor FOREIGN KEY (applied_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID(N'dbo.gate_deadline_change_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.gate_deadline_change_requests (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_gate_deadline_change_requests PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NOT NULL,
        requested_submission_due_at datetime2(0) NOT NULL,
        requested_review_end_at datetime2(0) NOT NULL,
        requested_revision_due_at datetime2(0) NOT NULL,
        reason nvarchar(2000) NOT NULL,
        status varchar(20) NOT NULL CONSTRAINT df_gate_dcr_status DEFAULT 'PENDING',
        requested_by bigint NOT NULL,
        reviewed_by bigint NULL,
        review_notes nvarchar(2000) NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_gate_dcr_created_at DEFAULT SYSUTCDATETIME(),
        reviewed_at datetime2(0) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_gate_dcr_token DEFAULT NEWID(),
        CONSTRAINT fk_gate_dcr_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_gate_dcr_gate FOREIGN KEY (gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT fk_gate_dcr_requester FOREIGN KEY (requested_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_gate_dcr_reviewer FOREIGN KEY (reviewed_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_gate_dcr_status CHECK (status IN ('PENDING','APPROVED','REJECTED')),
        CONSTRAINT ck_gate_dcr_order CHECK (requested_submission_due_at <= requested_review_end_at AND requested_review_end_at <= requested_revision_due_at)
    );
    CREATE INDEX ix_gate_dcr_project_gate ON dbo.gate_deadline_change_requests(project_id, gate_instance_id, status);
END;

-- 2. Gate 3-milestone deadlines + offset rule metadata (preserving legacy window_start/window_end)
IF COL_LENGTH(N'dbo.project_gate_instances', N'submission_due_at') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD submission_due_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.project_gate_instances', N'review_window_end_at') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD review_window_end_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.project_gate_instances', N'revision_due_at') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD revision_due_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.project_gate_instances', N'offset_rule_json') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD offset_rule_json nvarchar(1000) NULL;

-- 3. Template origin & customization preservation on milestones, sprints, tasks
IF COL_LENGTH(N'dbo.milestones', N'template_origin_id') IS NULL
    ALTER TABLE dbo.milestones ADD template_origin_id nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.milestones', N'is_user_customized') IS NULL
    ALTER TABLE dbo.milestones ADD is_user_customized bit NOT NULL CONSTRAINT df_milestones_is_user_customized DEFAULT 0;

IF COL_LENGTH(N'dbo.sprints', N'template_origin_id') IS NULL
    ALTER TABLE dbo.sprints ADD template_origin_id nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.sprints', N'is_user_customized') IS NULL
    ALTER TABLE dbo.sprints ADD is_user_customized bit NOT NULL CONSTRAINT df_sprints_is_user_customized DEFAULT 0;

IF COL_LENGTH(N'dbo.tasks', N'template_origin_id') IS NULL
    ALTER TABLE dbo.tasks ADD template_origin_id nvarchar(100) NULL;
IF COL_LENGTH(N'dbo.tasks', N'is_user_customized') IS NULL
    ALTER TABLE dbo.tasks ADD is_user_customized bit NOT NULL CONSTRAINT df_tasks_is_user_customized DEFAULT 0;
IF COL_LENGTH(N'dbo.tasks', N'is_core_gate_task') IS NULL
    ALTER TABLE dbo.tasks ADD is_core_gate_task bit NOT NULL CONSTRAINT df_tasks_is_core_gate_task DEFAULT 0;

COMMIT TRANSACTION;
GO
