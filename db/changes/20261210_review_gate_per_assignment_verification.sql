-- TK-W2-06: Per-assignment verification on review_gate_evidence_verifications (additive)
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.review_gate_evidence_verifications', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.review_gate_evidence_verifications', N'requirement_id') IS NULL
    BEGIN
        ALTER TABLE dbo.review_gate_evidence_verifications ADD requirement_id BIGINT NULL;
    END;
END;
GO

IF OBJECT_ID(N'dbo.review_gate_evidence_verifications', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.review_gate_evidence_verifications', N'requirement_id') IS NOT NULL
BEGIN
    UPDATE v
    SET v.requirement_id = e.requirement_id
    FROM dbo.review_gate_evidence_verifications v
    INNER JOIN dbo.review_gate_evidence e ON e.id = v.evidence_id
    WHERE v.requirement_id IS NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_review_gate_verification_requirement'
          AND parent_object_id = OBJECT_ID(N'dbo.review_gate_evidence_verifications')
    )
    BEGIN
        ALTER TABLE dbo.review_gate_evidence_verifications
            ADD CONSTRAINT fk_review_gate_verification_requirement
            FOREIGN KEY (requirement_id) REFERENCES dbo.project_gate_requirements(id);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.review_gate_evidence_verifications')
          AND name = N'ix_rg_ev_verify_assign_pkg_req'
    )
    BEGIN
        CREATE INDEX ix_rg_ev_verify_assign_pkg_req
            ON dbo.review_gate_evidence_verifications(assignment_id, package_id, requirement_id, id DESC);
    END;
END;
GO
