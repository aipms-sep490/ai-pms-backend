-- Additive and rerunnable. Legacy rubrics without levels stay readable and scorable.
-- Level count is data, not schema: a 4-level and a 5-level rubric both fit.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.rubric_criterion_levels', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rubric_criterion_levels (
            id                  BIGINT IDENTITY(1,1) NOT NULL,
            rubric_criterion_id BIGINT NOT NULL,
            level_order         INT NOT NULL,              -- 1 = thấp nhất
            label               NVARCHAR(100) NOT NULL,
            descriptor          NVARCHAR(1000) NOT NULL,
            score_from          DECIMAL(8,2) NULL,         -- dải GỢI Ý, không phải ngưỡng đạt
            score_to            DECIMAL(8,2) NULL,
            created_at          DATETIME2(0) NOT NULL
                CONSTRAINT df_rubric_criterion_levels_created_at DEFAULT (SYSUTCDATETIME()),
            updated_at          DATETIME2(0) NOT NULL
                CONSTRAINT df_rubric_criterion_levels_updated_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_rubric_criterion_levels PRIMARY KEY (id),
            CONSTRAINT uq_rubric_criterion_levels_order UNIQUE (rubric_criterion_id, level_order),
            CONSTRAINT ck_rubric_criterion_levels_order CHECK (level_order > 0),
            CONSTRAINT ck_rubric_criterion_levels_band
                CHECK (score_from IS NULL OR score_to IS NULL OR score_from <= score_to),
            CONSTRAINT fk_rubric_criterion_levels_criterion FOREIGN KEY (rubric_criterion_id)
                REFERENCES dbo.rubric_criteria(id) ON DELETE CASCADE ON UPDATE NO ACTION
        );
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.rubric_criterion_levels')
                   AND name = N'ix_rubric_criterion_levels_criterion')
        EXEC(N'CREATE INDEX ix_rubric_criterion_levels_criterion
               ON dbo.rubric_criterion_levels(rubric_criterion_id, level_order)');

    -- Hướng dẫn chấm và chuẩn đầu ra: thuộc tiêu chí TRONG MỘT rubric, không phải khái niệm dùng chung
    IF COL_LENGTH(N'dbo.rubric_criteria', N'learning_outcome') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD learning_outcome NVARCHAR(500) NULL;

    IF COL_LENGTH(N'dbo.rubric_criteria', N'evidence_guidance') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD evidence_guidance NVARCHAR(1000) NULL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
