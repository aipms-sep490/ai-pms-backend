-- BE-16 slice 2. Additive, rerunnable; no backfill of legacy FINAL_SUBMISSION projects.
-- Apply before deploying the new API. Does not publish grades or archive projects.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.final_submission_drafts', N'U') IS NULL
        THROW 51000, 'Apply final-submission drafts first.', 1;
    IF OBJECT_ID(N'dbo.final_submission_requirements', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_requirements (
            project_id BIGINT NOT NULL CONSTRAINT pk_final_submission_requirements PRIMARY KEY,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            updated_by BIGINT NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            CONSTRAINT fk_final_requirements_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_requirements_editor FOREIGN KEY (updated_by) REFERENCES dbo.users(id)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_requirement_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_requirement_items (
            project_id BIGINT NOT NULL,
            deliverable_id BIGINT NOT NULL,
            CONSTRAINT pk_final_submission_requirement_items PRIMARY KEY (project_id, deliverable_id),
            CONSTRAINT fk_final_requirement_items_policy FOREIGN KEY (project_id) REFERENCES dbo.final_submission_requirements(project_id),
            CONSTRAINT fk_final_requirement_items_deliverable FOREIGN KEY (deliverable_id) REFERENCES dbo.deliverables(id)
        );
        CREATE INDEX ix_final_requirement_items_deliverable ON dbo.final_submission_requirement_items(deliverable_id);
    END;
    IF OBJECT_ID(N'dbo.final_submissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submissions (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_final_submissions PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_final_submissions_project UNIQUE,
            project_period_id BIGINT NOT NULL,
            submitted_by BIGINT NOT NULL,
            submitted_at DATETIME2(0) NOT NULL,
            deadline DATETIME2(0) NOT NULL,
            notes NVARCHAR(MAX) NULL,
            draft_concurrency_token UNIQUEIDENTIFIER NOT NULL,
            requirements_concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_final_submissions_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_submissions_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_final_submissions_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_final_submissions_notes CHECK (notes IS NULL OR DATALENGTH(notes) <= 20000),
            CONSTRAINT ck_final_submissions_deadline CHECK (submitted_at < deadline)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_items (
            submission_id BIGINT NOT NULL,
            deliverable_version_id BIGINT NOT NULL,
            deliverable_id BIGINT NOT NULL,
            title NVARCHAR(255) NOT NULL,
            version_number INT NOT NULL,
            status_at_submission VARCHAR(20) NOT NULL,
            was_required BIT NOT NULL,
            files_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT pk_final_submission_items PRIMARY KEY (submission_id, deliverable_version_id),
            CONSTRAINT uq_final_submission_items_deliverable UNIQUE (submission_id, deliverable_id),
            CONSTRAINT fk_final_submission_items_submission FOREIGN KEY (submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_final_submission_items_version FOREIGN KEY (deliverable_version_id) REFERENCES dbo.deliverable_versions(id),
            CONSTRAINT fk_final_submission_items_deliverable FOREIGN KEY (deliverable_id) REFERENCES dbo.deliverables(id),
            CONSTRAINT ck_final_submission_items_version CHECK (version_number > 0),
            CONSTRAINT ck_final_submission_items_status CHECK (status_at_submission IN ('SUBMITTED','ACCEPTED')),
            CONSTRAINT ck_final_submission_items_files CHECK (ISJSON(files_json) = 1 AND files_json <> N'[]')
        );
        CREATE INDEX ix_final_submission_items_version ON dbo.final_submission_items(deliverable_version_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
