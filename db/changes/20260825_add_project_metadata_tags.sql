-- AI-PMS Migration Script
-- Date: 2026-08-25
-- Description: Add project metadata fields, rowversion concurrency, remove global team unique constraint, add unique filtered active team index, and normalized tags/project_tags tables.

USE [AI_PMS]; -- Default database
GO

SET XACT_ABORT ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '1. Dropping legacy constraint uq_projects_team if exists...';
    IF EXISTS (SELECT * FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.projects') AND name = N'uq_projects_team')
    BEGIN
        ALTER TABLE dbo.projects DROP CONSTRAINT uq_projects_team;
        PRINT 'Dropped constraint uq_projects_team.';
    END

    PRINT '2. Adding/modifying columns on dbo.projects...';

    -- problem_statement
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.projects') AND name = N'problem_statement')
    BEGIN
        ALTER TABLE dbo.projects ADD problem_statement NVARCHAR(MAX) NULL;
        PRINT 'Added column [problem_statement] to [dbo].[projects].';
    END

    -- expected_output (Ensure NVARCHAR(MAX))
    IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.projects') AND name = N'expected_output')
    BEGIN
        ALTER TABLE dbo.projects ALTER COLUMN expected_output NVARCHAR(MAX) NULL;
        PRINT 'Altered column [expected_output] to NVARCHAR(MAX) on [dbo].[projects].';
    END
    ELSE
    BEGIN
        ALTER TABLE dbo.projects ADD expected_output NVARCHAR(MAX) NULL;
        PRINT 'Added column [expected_output] as NVARCHAR(MAX) to [dbo].[projects].';
    END

    -- row_version
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.projects') AND name = N'row_version')
    BEGIN
        ALTER TABLE dbo.projects ADD row_version ROWVERSION NOT NULL;
        PRINT 'Added column [row_version] to [dbo].[projects].';
    END

    PRINT '3. Creating unique filtered index uq_projects_active_team on dbo.projects...';
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.projects') AND name = N'uq_projects_active_team')
    BEGIN
        CREATE UNIQUE NONCLUSTERED INDEX uq_projects_active_team
        ON dbo.projects (team_id)
        WHERE status IN (
            N'DRAFT', N'SUBMITTED', N'UNDER_REVIEW', N'REVISION_REQUIRED',
            N'APPROVED', N'SUPERVISOR_PENDING', N'ACTIVE', N'FINAL_SUBMISSION'
        );
        PRINT 'Created index uq_projects_active_team.';
    END

    PRINT '4. Creating dbo.tags table...';
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.tags') AND type in (N'U'))
    BEGIN
        CREATE TABLE dbo.tags (
            id              BIGINT IDENTITY(1,1) NOT NULL,
            name            NVARCHAR(100) NOT NULL,
            normalized_name NVARCHAR(100) NOT NULL,
            tag_type        NVARCHAR(30) NOT NULL,
            created_at      DATETIME2(0) NOT NULL CONSTRAINT df_tags_created_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_tags PRIMARY KEY (id),
            CONSTRAINT uq_tags_normalized_name_type UNIQUE (normalized_name, tag_type),
            CONSTRAINT ck_tags_type CHECK (tag_type IN (N'DOMAIN', N'TECHNOLOGY', N'KEYWORD'))
        );
        PRINT 'Created table [dbo].[tags].';
    END

    PRINT '5. Creating dbo.project_tags table...';
    IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.project_tags') AND type in (N'U'))
    BEGIN
        CREATE TABLE dbo.project_tags (
            project_id      BIGINT NOT NULL,
            tag_id          BIGINT NOT NULL,
            created_at      DATETIME2(0) NOT NULL CONSTRAINT df_project_tags_created_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_project_tags PRIMARY KEY CLUSTERED (project_id, tag_id),
            CONSTRAINT fk_project_tags_project FOREIGN KEY (project_id)
                REFERENCES dbo.projects(id) ON DELETE CASCADE ON UPDATE NO ACTION,
            CONSTRAINT fk_project_tags_tag FOREIGN KEY (tag_id)
                REFERENCES dbo.tags(id) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
        PRINT 'Created table [dbo].[project_tags].';
    END

    PRINT '6. Creating indexes for tags and project_tags...';
    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.project_tags') AND name = N'ix_project_tags_tag_project')
    BEGIN
        CREATE NONCLUSTERED INDEX ix_project_tags_tag_project ON dbo.project_tags (tag_id, project_id);
        PRINT 'Created index [ix_project_tags_tag_project] on [dbo].[project_tags].';
    END

    COMMIT TRANSACTION;
    PRINT 'Migration completed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
    BEGIN
        ROLLBACK TRANSACTION;
        PRINT 'Transaction rolled back due to error.';
    END
    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();
    RAISERROR(@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH
GO
