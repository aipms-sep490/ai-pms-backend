SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.review_gate_assignments', N'revoked_at') IS NULL
    ALTER TABLE dbo.review_gate_assignments ADD revoked_at DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.review_gate_assignments', N'revoked_by') IS NULL
    ALTER TABLE dbo.review_gate_assignments ADD revoked_by BIGINT NULL;
IF COL_LENGTH(N'dbo.review_gate_assignments', N'revocation_reason') IS NULL
    ALTER TABLE dbo.review_gate_assignments ADD revocation_reason NVARCHAR(2000) NULL;
IF COL_LENGTH(N'dbo.project_gate_instances', N'block_next_gate') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD block_next_gate BIT NOT NULL CONSTRAINT df_project_gate_block_next DEFAULT 1;
IF COL_LENGTH(N'dbo.project_gate_instances', N'blocks_defense') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD blocks_defense BIT NOT NULL CONSTRAINT df_project_gate_blocks_defense DEFAULT 0;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_review_gate_assignment_revoked_by')
    ALTER TABLE dbo.review_gate_assignments ADD CONSTRAINT fk_review_gate_assignment_revoked_by FOREIGN KEY(revoked_by) REFERENCES dbo.users(id);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_review_gate_assignment_revocation')
    ALTER TABLE dbo.review_gate_assignments ADD CONSTRAINT ck_review_gate_assignment_revocation CHECK (
        (status <> 'REVOKED' AND revoked_at IS NULL AND revoked_by IS NULL AND revocation_reason IS NULL)
        OR (status = 'REVOKED' AND revoked_at IS NOT NULL AND revoked_by IS NOT NULL AND LEN(LTRIM(RTRIM(revocation_reason))) > 0));
COMMIT TRANSACTION;
