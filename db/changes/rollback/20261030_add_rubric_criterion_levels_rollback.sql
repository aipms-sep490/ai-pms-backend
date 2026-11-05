-- Rollback for 20261030_add_rubric_criterion_levels.sql
-- Drops dbo.rubric_criterion_levels table and removes learning_outcome, evidence_guidance columns.
-- Idempotent and rerunnable.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.rubric_criterion_levels', N'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.rubric_criterion_levels;
    END;

    IF COL_LENGTH(N'dbo.rubric_criteria', N'evidence_guidance') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.rubric_criteria DROP COLUMN evidence_guidance;
    END;

    IF COL_LENGTH(N'dbo.rubric_criteria', N'learning_outcome') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.rubric_criteria DROP COLUMN learning_outcome;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
