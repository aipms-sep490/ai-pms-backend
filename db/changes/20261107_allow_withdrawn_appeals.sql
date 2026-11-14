SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.result_appeal_requests', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_appeal_request_status' AND parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests'))
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT ck_appeal_request_status;
    ALTER TABLE dbo.result_appeal_requests WITH CHECK ADD CONSTRAINT ck_appeal_request_status
        CHECK (status IN ('PENDING', 'UNDER_REVIEW', 'ACCEPTED', 'REJECTED', 'WITHDRAWN'));
END;

COMMIT TRANSACTION;
