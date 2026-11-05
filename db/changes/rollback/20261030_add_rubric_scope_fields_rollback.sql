-- Rollback for 20261030_add_rubric_scope_fields.sql
-- Drops scope/stage/major fields, index, and check constraints on dbo.rubrics.
-- Drops foreign key fk_rubrics_major before dropping major_id column.
-- Idempotent and rerunnable.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'ix_rubrics_scope')
    BEGIN
        DROP INDEX ix_rubrics_scope ON dbo.rubrics;
    END;

    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'dbo.fk_rubrics_major'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT fk_rubrics_major;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_effective_range'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT ck_rubrics_effective_range;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_scope_major'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT ck_rubrics_scope_major;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_evaluation_scope'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT ck_rubrics_evaluation_scope;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_evaluation_stage'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT ck_rubrics_evaluation_stage;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_project_mode'))
    BEGIN
        ALTER TABLE dbo.rubrics DROP CONSTRAINT ck_rubrics_project_mode;
    END;

    IF COL_LENGTH(N'dbo.rubrics', N'approval_reference') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN approval_reference;

    IF COL_LENGTH(N'dbo.rubrics', N'effective_to') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN effective_to;

    IF COL_LENGTH(N'dbo.rubrics', N'effective_from') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN effective_from;

    IF COL_LENGTH(N'dbo.rubrics', N'evaluation_scope') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN evaluation_scope;

    IF COL_LENGTH(N'dbo.rubrics', N'evaluation_stage') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN evaluation_stage;

    IF COL_LENGTH(N'dbo.rubrics', N'project_mode') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN project_mode;

    IF COL_LENGTH(N'dbo.rubrics', N'major_id') IS NOT NULL
        ALTER TABLE dbo.rubrics DROP COLUMN major_id;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
