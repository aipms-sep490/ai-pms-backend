-- Rollback for 20261030_add_rubric_approvals.sql
-- Restores ck_rubric_versions_status check constraint to ('DRAFT','PUBLISHED','RETIRED')
-- and drops dbo.rubric_approvals tracking table.
-- Guards against rolling back if existing rubric_versions records have status 'PENDING_APPROVAL'.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM dbo.rubric_versions WHERE status = 'PENDING_APPROVAL')
    BEGIN
        RAISERROR('Cannot rollback rubric approvals: existing rubric_versions records have status PENDING_APPROVAL. Revert or transition these records before rolling back.', 16, 1);
    END;

    IF OBJECT_ID(N'dbo.rubric_approvals', N'U') IS NOT NULL
    BEGIN
        DROP TABLE dbo.rubric_approvals;
    END;

    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_versions')
               AND name = N'ck_rubric_versions_status')
    BEGIN
        ALTER TABLE dbo.rubric_versions DROP CONSTRAINT ck_rubric_versions_status;
    END;

    ALTER TABLE dbo.rubric_versions ADD CONSTRAINT ck_rubric_versions_status
        CHECK (status IN ('DRAFT','PUBLISHED','RETIRED'));

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
