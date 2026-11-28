-- Rollback for 20261212_allow_force_lock_after_deadline_and_final_reopen.sql
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
            CHECK (submitted_at < deadline);
END;
GO
