-- Additive and rerunnable; existing criteria remain roots with unchanged scores.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.rubric_criteria', N'parent_id') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD parent_id BIGINT NULL;

    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.rubric_criteria')
               AND name = N'max_score' AND is_nullable = 0)
        ALTER TABLE dbo.rubric_criteria ALTER COLUMN max_score DECIMAL(8,2) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_criteria')
                   AND name = N'uq_rubric_criteria_id_rubric')
        ALTER TABLE dbo.rubric_criteria ADD CONSTRAINT uq_rubric_criteria_id_rubric UNIQUE (id, rubric_id);

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_criteria')
                   AND name = N'fk_rubric_criteria_parent')
        EXEC(N'ALTER TABLE dbo.rubric_criteria ADD CONSTRAINT fk_rubric_criteria_parent
            FOREIGN KEY (parent_id, rubric_id) REFERENCES dbo.rubric_criteria(id, rubric_id)');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_criteria')
                   AND name = N'ck_rubric_criteria_parent')
        EXEC(N'ALTER TABLE dbo.rubric_criteria ADD CONSTRAINT ck_rubric_criteria_parent CHECK (parent_id <> id)');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rubric_criteria')
                   AND name = N'ix_rubric_criteria_parent_id')
        EXEC(N'CREATE INDEX ix_rubric_criteria_parent_id ON dbo.rubric_criteria(parent_id)');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
