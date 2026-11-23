-- PR-P0-A (P0.3): Assignment validity windows and scope lookup indexes.
-- Additive, nullable for 100% backward compatibility with existing assignments.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.evaluation_assignments', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.evaluation_assignments', N'valid_from') IS NULL
        ALTER TABLE dbo.evaluation_assignments ADD valid_from datetime2(0) NULL;
    IF COL_LENGTH(N'dbo.evaluation_assignments', N'valid_until') IS NULL
        ALTER TABLE dbo.evaluation_assignments ADD valid_until datetime2(0) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_evaluation_assignments_validity' AND parent_object_id = OBJECT_ID(N'dbo.evaluation_assignments'))
        EXEC(N'ALTER TABLE dbo.evaluation_assignments ADD CONSTRAINT ck_evaluation_assignments_validity CHECK (valid_from IS NULL OR valid_until IS NULL OR valid_until >= valid_from);');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_evaluation_assignments_validity' AND object_id = OBJECT_ID(N'dbo.evaluation_assignments'))
        EXEC(N'CREATE INDEX ix_evaluation_assignments_validity ON dbo.evaluation_assignments(evaluator_id, project_id, status, valid_from, valid_until);');
END;

IF OBJECT_ID(N'dbo.review_gate_assignments', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.review_gate_assignments', N'valid_from') IS NULL
        ALTER TABLE dbo.review_gate_assignments ADD valid_from datetime2(0) NULL;
    IF COL_LENGTH(N'dbo.review_gate_assignments', N'valid_until') IS NULL
        ALTER TABLE dbo.review_gate_assignments ADD valid_until datetime2(0) NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_review_gate_assignments_validity' AND parent_object_id = OBJECT_ID(N'dbo.review_gate_assignments'))
        EXEC(N'ALTER TABLE dbo.review_gate_assignments ADD CONSTRAINT ck_review_gate_assignments_validity CHECK (valid_from IS NULL OR valid_until IS NULL OR valid_until >= valid_from);');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_review_gate_assignments_validity' AND object_id = OBJECT_ID(N'dbo.review_gate_assignments'))
        EXEC(N'CREATE INDEX ix_review_gate_assignments_validity ON dbo.review_gate_assignments(reviewer_id, gate_instance_id, status, valid_from, valid_until);');
END;

COMMIT TRANSACTION;
GO
