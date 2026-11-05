-- Additive and rerunnable. Seven scope/stage/major fields on dbo.rubrics.
-- All columns are nullable without backfill to protect historical rows.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.rubrics', N'major_id') IS NULL
        ALTER TABLE dbo.rubrics ADD major_id BIGINT NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'project_mode') IS NULL
        ALTER TABLE dbo.rubrics ADD project_mode NVARCHAR(32) NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'evaluation_stage') IS NULL
        ALTER TABLE dbo.rubrics ADD evaluation_stage NVARCHAR(32) NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'evaluation_scope') IS NULL
        ALTER TABLE dbo.rubrics ADD evaluation_scope NVARCHAR(32) NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'effective_from') IS NULL
        ALTER TABLE dbo.rubrics ADD effective_from DATE NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'effective_to') IS NULL
        ALTER TABLE dbo.rubrics ADD effective_to DATE NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'approval_reference') IS NULL
        ALTER TABLE dbo.rubrics ADD approval_reference NVARCHAR(1000) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE object_id = OBJECT_ID(N'dbo.fk_rubrics_major'))
        ALTER TABLE dbo.rubrics ADD CONSTRAINT fk_rubrics_major
            FOREIGN KEY (major_id) REFERENCES dbo.majors(id) ON DELETE NO ACTION ON UPDATE NO ACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_project_mode'))
        EXEC(N'ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_project_mode
            CHECK (project_mode IS NULL OR project_mode IN (''SINGLE_MAJOR'',''INTERDISCIPLINARY''))');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_evaluation_stage'))
        EXEC(N'ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_evaluation_stage
            CHECK (evaluation_stage IS NULL OR evaluation_stage IN (''ON_GOING'',''COLD'',''DEFENSE''))');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_evaluation_scope'))
        EXEC(N'ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_evaluation_scope
            CHECK (evaluation_scope IS NULL OR evaluation_scope IN (''COMMON'',''MAJOR_SPECIFIC'',''INDIVIDUAL''))');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_scope_major'))
        EXEC(N'ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_scope_major
            CHECK (evaluation_scope IS NULL OR evaluation_scope <> ''COMMON'' OR major_id IS NULL)');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE object_id = OBJECT_ID(N'dbo.ck_rubrics_effective_range'))
        EXEC(N'ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_effective_range
            CHECK (effective_from IS NULL OR effective_to IS NULL OR effective_from <= effective_to)');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'ix_rubrics_scope')
        EXEC(N'CREATE INDEX ix_rubrics_scope ON dbo.rubrics(academic_semester_id, project_mode, evaluation_stage, evaluation_scope, major_id)');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
