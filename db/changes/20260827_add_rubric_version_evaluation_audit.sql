-- AI-PMS Migration Script
-- Date: 2026-08-27
-- Description: Add approved rubric versions, evaluation evidence/finalization audit, deterministic rounding metadata, and optimistic concurrency tokens.
-- Review first. Do not run against the shared SQL Server before approval.

USE [AI_PMS];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.rubrics', N'version_number') IS NULL
        ALTER TABLE dbo.rubrics ADD version_number INT NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'approval_status') IS NULL
        ALTER TABLE dbo.rubrics ADD approval_status NVARCHAR(20) NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'approved_by') IS NULL
        ALTER TABLE dbo.rubrics ADD approved_by BIGINT NULL;

    IF COL_LENGTH(N'dbo.rubrics', N'approved_at') IS NULL
        ALTER TABLE dbo.rubrics ADD approved_at DATETIME2(0) NULL;

    EXEC sys.sp_executesql N'
        UPDATE dbo.rubrics
        SET version_number = COALESCE(version_number, 1),
            approval_status = COALESCE(approval_status, CASE WHEN is_active = 1 THEN N''APPROVED'' ELSE N''INACTIVE'' END),
            approved_by = COALESCE(approved_by, created_by),
            approved_at = COALESCE(approved_at, created_at)
        WHERE version_number IS NULL
           OR approval_status IS NULL
           OR approved_by IS NULL
           OR approved_at IS NULL;';

    ALTER TABLE dbo.rubrics ALTER COLUMN version_number INT NOT NULL;
    ALTER TABLE dbo.rubrics ALTER COLUMN approval_status NVARCHAR(20) NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'df_rubrics_version_number')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT df_rubrics_version_number DEFAULT (1) FOR version_number;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'df_rubrics_approval_status')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT df_rubrics_approval_status DEFAULT (N'DRAFT') FOR approval_status;

    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'df_rubrics_is_active')
        ALTER TABLE dbo.rubrics DROP CONSTRAINT df_rubrics_is_active;

    ALTER TABLE dbo.rubrics ADD CONSTRAINT df_rubrics_is_active DEFAULT (0) FOR is_active;

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'uq_rubrics_code')
        ALTER TABLE dbo.rubrics DROP CONSTRAINT uq_rubrics_code;

    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'uq_rubrics_code_version')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT uq_rubrics_code_version UNIQUE (code, version_number);

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'ck_rubrics_version_number')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_version_number CHECK (version_number > 0);

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'ck_rubrics_approval_status')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_approval_status CHECK (approval_status IN (N'DRAFT', N'APPROVED', N'INACTIVE'));

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'ck_rubrics_approval_audit')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT ck_rubrics_approval_audit CHECK (
            (approval_status = N'DRAFT' AND approved_by IS NULL AND approved_at IS NULL AND is_active = 0)
            OR (approval_status IN (N'APPROVED', N'INACTIVE') AND approved_by IS NOT NULL AND approved_at IS NOT NULL)
        );

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics') AND name = N'fk_rubrics_approved_by')
        ALTER TABLE dbo.rubrics ADD CONSTRAINT fk_rubrics_approved_by FOREIGN KEY (approved_by) REFERENCES dbo.users(id);

    IF COL_LENGTH(N'dbo.rubrics', N'row_version') IS NULL
        ALTER TABLE dbo.rubrics ADD row_version ROWVERSION NOT NULL;

    IF COL_LENGTH(N'dbo.evaluations', N'evidence_summary') IS NULL
        ALTER TABLE dbo.evaluations ADD evidence_summary NVARCHAR(MAX) NULL;

    IF COL_LENGTH(N'dbo.evaluations', N'finalized_by') IS NULL
        ALTER TABLE dbo.evaluations ADD finalized_by BIGINT NULL;

    IF COL_LENGTH(N'dbo.evaluations', N'finalized_at') IS NULL
        ALTER TABLE dbo.evaluations ADD finalized_at DATETIME2(0) NULL;

    IF COL_LENGTH(N'dbo.evaluations', N'rounding_rule') IS NULL
        ALTER TABLE dbo.evaluations ADD rounding_rule NVARCHAR(30) NULL;

    UPDATE dbo.evaluations
    SET rounding_rule = COALESCE(rounding_rule, N'AWAY_FROM_ZERO_2DP'),
        finalized_by = CASE WHEN status = N'FINALIZED' THEN COALESCE(finalized_by, evaluator_id) ELSE NULL END,
        finalized_at = CASE WHEN status = N'FINALIZED' THEN COALESCE(finalized_at, evaluated_at, updated_at) ELSE NULL END;

    ALTER TABLE dbo.evaluations ALTER COLUMN rounding_rule NVARCHAR(30) NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.evaluations') AND name = N'df_evaluations_rounding_rule')
        ALTER TABLE dbo.evaluations ADD CONSTRAINT df_evaluations_rounding_rule DEFAULT (N'AWAY_FROM_ZERO_2DP') FOR rounding_rule;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.evaluations') AND name = N'ck_evaluations_rounding_rule')
        ALTER TABLE dbo.evaluations ADD CONSTRAINT ck_evaluations_rounding_rule CHECK (rounding_rule = N'AWAY_FROM_ZERO_2DP');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.evaluations') AND name = N'ck_evaluations_finalize_audit')
        ALTER TABLE dbo.evaluations ADD CONSTRAINT ck_evaluations_finalize_audit CHECK (
            (status <> N'FINALIZED' AND finalized_by IS NULL AND finalized_at IS NULL)
            OR (status = N'FINALIZED' AND finalized_by IS NOT NULL AND finalized_at IS NOT NULL)
        );

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.evaluations') AND name = N'fk_evaluations_finalized_by')
        ALTER TABLE dbo.evaluations ADD CONSTRAINT fk_evaluations_finalized_by FOREIGN KEY (finalized_by) REFERENCES dbo.users(id);

    IF COL_LENGTH(N'dbo.evaluations', N'row_version') IS NULL
        ALTER TABLE dbo.evaluations ADD row_version ROWVERSION NOT NULL;

    COMMIT TRANSACTION;
    PRINT N'BE-09 rubric/evaluation schema change completed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
