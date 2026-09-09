-- AI-PMS Migration Script
-- Date: 2026-09-09
-- Description: Add policy configuration fields (min/max team size, min distinct majors, supervisor quota, milestone template ID, rubric ID) to dbo.project_periods.

USE [AI_PMS];
GO

SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    PRINT '1. Adding policy & reference columns to dbo.project_periods...';

    -- min_team_size
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'min_team_size')
    BEGIN
        ALTER TABLE dbo.project_periods ADD min_team_size INT NULL CONSTRAINT df_project_periods_min_team_size DEFAULT (3);
        PRINT 'Added column [min_team_size] to [dbo].[project_periods].';
    END

    -- max_team_size
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'max_team_size')
    BEGIN
        ALTER TABLE dbo.project_periods ADD max_team_size INT NULL CONSTRAINT df_project_periods_max_team_size DEFAULT (5);
        PRINT 'Added column [max_team_size] to [dbo].[project_periods].';
    END

    -- min_distinct_majors
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'min_distinct_majors')
    BEGIN
        ALTER TABLE dbo.project_periods ADD min_distinct_majors INT NULL CONSTRAINT df_project_periods_min_distinct_majors DEFAULT (1);
        PRINT 'Added column [min_distinct_majors] to [dbo].[project_periods].';
    END

    -- max_projects_per_supervisor
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'max_projects_per_supervisor')
    BEGIN
        ALTER TABLE dbo.project_periods ADD max_projects_per_supervisor INT NULL CONSTRAINT df_project_periods_max_projects_per_supervisor DEFAULT (5);
        PRINT 'Added column [max_projects_per_supervisor] to [dbo].[project_periods].';
    END

    -- milestone_template_id
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'milestone_template_id')
    BEGIN
        ALTER TABLE dbo.project_periods ADD milestone_template_id BIGINT NULL;
        PRINT 'Added column [milestone_template_id] to [dbo].[project_periods].';
    END

    -- rubric_id
    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'rubric_id')
    BEGIN
        ALTER TABLE dbo.project_periods ADD rubric_id BIGINT NULL;
        PRINT 'Added column [rubric_id] to [dbo].[project_periods].';
    END

    PRINT '2. Adding CHECK constraints to dbo.project_periods...';

    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'ck_project_periods_team_size')
    BEGIN
        ALTER TABLE dbo.project_periods ADD CONSTRAINT ck_project_periods_team_size CHECK (
            (min_team_size IS NULL AND max_team_size IS NULL) OR
            (min_team_size >= 1 AND max_team_size >= min_team_size)
        );
        PRINT 'Added constraint ck_project_periods_team_size.';
    END

    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'ck_project_periods_majors')
    BEGIN
        ALTER TABLE dbo.project_periods ADD CONSTRAINT ck_project_periods_majors CHECK (
            min_distinct_majors IS NULL OR
            (min_distinct_majors >= 1 AND (max_team_size IS NULL OR min_distinct_majors <= max_team_size))
        );
        PRINT 'Added constraint ck_project_periods_majors.';
    END

    IF NOT EXISTS (SELECT * FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'ck_project_periods_supervisor')
    BEGIN
        ALTER TABLE dbo.project_periods ADD CONSTRAINT ck_project_periods_supervisor CHECK (
            max_projects_per_supervisor IS NULL OR max_projects_per_supervisor >= 1
        );
        PRINT 'Added constraint ck_project_periods_supervisor.';
    END

    PRINT '3. Adding Foreign Keys to dbo.project_periods...';

    IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.project_periods') AND name = N'fk_project_periods_rubric')
    BEGIN
        ALTER TABLE dbo.project_periods ADD CONSTRAINT fk_project_periods_rubric FOREIGN KEY (rubric_id)
            REFERENCES dbo.rubrics(id) ON DELETE NO ACTION ON UPDATE NO ACTION;
        PRINT 'Added foreign key fk_project_periods_rubric.';
    END

    COMMIT TRANSACTION;
    PRINT 'Migration 20260909_add_project_period_policy_fields executed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
    DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
    DECLARE @ErrorState INT = ERROR_STATE();

    RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
END CATCH;
GO
