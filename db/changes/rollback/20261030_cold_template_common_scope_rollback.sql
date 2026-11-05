-- Rollback for 20261030_cold_template_common_scope.sql
-- Reverts major_id column on dbo.cold_report_templates to NOT NULL,
-- drops scope column and check constraints ck_cold_template_scope, ck_cold_template_scope_major.
-- Guards against rolling back if existing cold_report_templates have major_id IS NULL.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.cold_report_templates WHERE major_id IS NULL)
    BEGIN
        RAISERROR('Cannot rollback cold_template_common_scope: existing cold_report_templates records have major_id IS NULL (COMMON scope). Reassign or remove these templates before rolling back.', 16, 1);
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates')
               AND name = N'ck_cold_template_scope_major')
    BEGIN
        ALTER TABLE dbo.cold_report_templates DROP CONSTRAINT ck_cold_template_scope_major;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates')
               AND name = N'ck_cold_template_scope')
    BEGIN
        ALTER TABLE dbo.cold_report_templates DROP CONSTRAINT ck_cold_template_scope;
    END;

    IF COL_LENGTH(N'dbo.cold_report_templates', N'scope') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.cold_report_templates DROP COLUMN scope;
    END;

    IF EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'dbo.cold_report_templates')
               AND name = N'major_id' AND is_nullable = 1)
    BEGIN
        ALTER TABLE dbo.cold_report_templates ALTER COLUMN major_id BIGINT NOT NULL;
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
