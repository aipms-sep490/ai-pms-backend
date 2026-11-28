-- Rollback TK-W2-06: Per-assignment verification on review_gate_evidence_verifications
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.review_gate_evidence_verifications')
      AND name = N'ix_rg_ev_verify_assign_pkg_req'
)
    DROP INDEX ix_rg_ev_verify_assign_pkg_req ON dbo.review_gate_evidence_verifications;

IF EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'fk_review_gate_verification_requirement'
      AND parent_object_id = OBJECT_ID(N'dbo.review_gate_evidence_verifications')
)
    ALTER TABLE dbo.review_gate_evidence_verifications DROP CONSTRAINT fk_review_gate_verification_requirement;

IF COL_LENGTH(N'dbo.review_gate_evidence_verifications', N'requirement_id') IS NOT NULL
    ALTER TABLE dbo.review_gate_evidence_verifications DROP COLUMN requirement_id;
GO
