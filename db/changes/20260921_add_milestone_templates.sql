SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.milestone_templates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_templates (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_templates PRIMARY KEY,
        name NVARCHAR(255) NOT NULL,
        description NVARCHAR(MAX) NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_milestone_templates_status DEFAULT (N'ACTIVE'),
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_templates_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_templates_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_templates_status CHECK (status IN (N'ACTIVE', N'INACTIVE')),
        CONSTRAINT fk_milestone_templates_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;
IF OBJECT_ID(N'dbo.milestone_template_versions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_template_versions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_template_versions PRIMARY KEY,
        milestone_template_id BIGINT NOT NULL,
        version_number INT NOT NULL,
        status NVARCHAR(20) NOT NULL CONSTRAINT df_milestone_template_versions_status DEFAULT (N'DRAFT'),
        created_by BIGINT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_versions_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_versions_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_template_versions_status CHECK (status IN (N'DRAFT', N'PUBLISHED', N'ARCHIVED')),
        CONSTRAINT uq_milestone_template_versions_template_version UNIQUE (milestone_template_id, version_number),
        CONSTRAINT fk_milestone_template_versions_template FOREIGN KEY (milestone_template_id) REFERENCES dbo.milestone_templates(id),
        CONSTRAINT fk_milestone_template_versions_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id)
    );
END;
IF OBJECT_ID(N'dbo.milestone_template_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_template_items (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_milestone_template_items PRIMARY KEY,
        milestone_template_version_id BIGINT NOT NULL,
        title NVARCHAR(255) NOT NULL,
        description NVARCHAR(MAX) NULL,
        start_offset_days INT NULL,
        due_offset_days INT NULL,
        sort_order INT NOT NULL,
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_items_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_template_items_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT ck_milestone_template_items_offsets CHECK (due_offset_days IS NULL OR start_offset_days IS NULL OR due_offset_days >= start_offset_days),
        CONSTRAINT fk_milestone_template_items_version FOREIGN KEY (milestone_template_version_id) REFERENCES dbo.milestone_template_versions(id) ON DELETE CASCADE
    );
END;
IF OBJECT_ID(N'dbo.project_milestone_template_applications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_milestone_template_applications (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_milestone_template_applications PRIMARY KEY,
        project_id BIGINT NOT NULL,
        milestone_template_version_id BIGINT NOT NULL,
        applied_by BIGINT NOT NULL,
        applied_at DATETIME2(0) NOT NULL CONSTRAINT df_project_milestone_template_applications_applied_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT uq_project_milestone_template_applications_project UNIQUE (project_id),
        CONSTRAINT fk_project_milestone_template_applications_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id) ON DELETE CASCADE,
        CONSTRAINT fk_project_milestone_template_applications_version FOREIGN KEY (milestone_template_version_id) REFERENCES dbo.milestone_template_versions(id),
        CONSTRAINT fk_project_milestone_template_applications_user FOREIGN KEY (applied_by) REFERENCES dbo.users(id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_project_periods_milestone_template')
    ALTER TABLE dbo.project_periods ADD CONSTRAINT fk_project_periods_milestone_template FOREIGN KEY (milestone_template_id) REFERENCES dbo.milestone_templates(id);
CREATE INDEX ix_milestone_template_versions_template_status ON dbo.milestone_template_versions(milestone_template_id, status);
CREATE INDEX ix_milestone_template_items_version_sort ON dbo.milestone_template_items(milestone_template_version_id, sort_order);
COMMIT TRANSACTION;
