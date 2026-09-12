-- BE-16 slice 1: draft selections only, not official locked submissions.
-- Prerequisite: canonical schema.sql. Additive and rerunnable; no legacy backfill.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.projects', N'U') IS NULL OR OBJECT_ID(N'dbo.deliverable_versions', N'U') IS NULL
        THROW 51000, 'Apply the canonical schema before final-submission drafts.', 1;

    IF OBJECT_ID(N'dbo.final_submission_drafts', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_drafts (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_final_submission_drafts PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_final_submission_drafts_project UNIQUE,
            project_period_id BIGINT NOT NULL,
            notes NVARCHAR(MAX) NULL,
            created_by BIGINT NOT NULL,
            updated_by BIGINT NOT NULL,
            created_at DATETIME2(0) NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_final_submission_drafts_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_final_submission_drafts_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_final_submission_drafts_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_final_submission_drafts_editor FOREIGN KEY (updated_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_final_submission_drafts_notes CHECK (notes IS NULL OR DATALENGTH(notes) <= 20000)
        );
    END;
    IF OBJECT_ID(N'dbo.final_submission_draft_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.final_submission_draft_items (
            draft_id BIGINT NOT NULL,
            deliverable_version_id BIGINT NOT NULL,
            CONSTRAINT pk_final_submission_draft_items PRIMARY KEY (draft_id, deliverable_version_id),
            CONSTRAINT fk_final_submission_draft_items_draft FOREIGN KEY (draft_id) REFERENCES dbo.final_submission_drafts(id),
            CONSTRAINT fk_final_submission_draft_items_version FOREIGN KEY (deliverable_version_id) REFERENCES dbo.deliverable_versions(id)
        );
        CREATE INDEX ix_final_submission_draft_items_version ON dbo.final_submission_draft_items(deliverable_version_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
