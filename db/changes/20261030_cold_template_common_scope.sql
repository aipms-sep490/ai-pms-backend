-- Additive and rerunnable; existing cold report templates remain valid with scope = NULL.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cold_report_templates')
               AND name = N'major_id' AND is_nullable = 0)
        ALTER TABLE dbo.cold_report_templates ALTER COLUMN major_id BIGINT NULL;

    IF COL_LENGTH(N'dbo.cold_report_templates', N'scope') IS NULL
        ALTER TABLE dbo.cold_report_templates ADD scope NVARCHAR(32) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates')
                   AND name = N'ck_cold_template_scope')
        EXEC(N'ALTER TABLE dbo.cold_report_templates ADD CONSTRAINT ck_cold_template_scope
            CHECK (scope IS NULL OR scope IN (''COMMON'',''MAJOR_SPECIFIC'',''INDIVIDUAL''))');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates')
                   AND name = N'ck_cold_template_scope_major')
        EXEC(N'ALTER TABLE dbo.cold_report_templates ADD CONSTRAINT ck_cold_template_scope_major
            CHECK (scope <> ''COMMON'' OR major_id IS NULL)');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
