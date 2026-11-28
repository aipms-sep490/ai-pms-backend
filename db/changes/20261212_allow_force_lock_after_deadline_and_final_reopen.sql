-- Wave 3 (TK-W3-01): Allow Lead Department Force-Lock after deadline (submitted_at < deadline OR force_locked_by IS NOT NULL)
-- Safe additive check constraint update (100% idempotent).

SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.final_submissions', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.final_submissions')
          AND name = N'ck_final_submissions_deadline'
    )
        ALTER TABLE dbo.final_submissions DROP CONSTRAINT ck_final_submissions_deadline;
END;
GO

IF OBJECT_ID(N'dbo.final_submissions', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.final_submissions')
          AND name = N'ck_final_submissions_deadline'
    )
        ALTER TABLE dbo.final_submissions
            ADD CONSTRAINT ck_final_submissions_deadline
            CHECK (submitted_at < deadline OR force_locked_by IS NOT NULL);
END;
GO
