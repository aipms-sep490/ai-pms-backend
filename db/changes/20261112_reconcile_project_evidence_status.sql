SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- The API projects the latest review decision at read time. This preserves
-- source-version/hash semantics for legacy rows whose source may have changed
-- since a review. New reviews also persist the mapped status in the same
-- transaction.
IF OBJECT_ID('dbo.project_evidence', 'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('dbo.project_evidence')
          AND name = 'ck_evidence_status'
    )
        ALTER TABLE dbo.project_evidence DROP CONSTRAINT ck_evidence_status;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID('dbo.project_evidence')
          AND name = 'ck_evidence_status'
    )
        ALTER TABLE dbo.project_evidence ADD CONSTRAINT ck_evidence_status
            CHECK(verification_status IN ('PENDING','UNKNOWN','VERIFIED','REJECTED'));

END;

-- Verification query (read-only): the API should return the mapped status for
-- every row returned by this query without changing the stored source snapshot.
-- SELECT e.id, e.verification_status, r.decision
-- FROM dbo.project_evidence e
-- JOIN (SELECT evidence_id, decision, ROW_NUMBER() OVER (PARTITION BY evidence_id ORDER BY id DESC) rn
--       FROM dbo.project_evidence_reviews) r ON r.evidence_id=e.id AND r.rn=1;

COMMIT;
GO
