SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.supervisor_requests', N'expires_at') IS NULL
BEGIN
    ALTER TABLE dbo.supervisor_requests ADD expires_at DATETIME2(0) NULL;
END;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_supervisor_requests_status' AND parent_object_id = OBJECT_ID(N'dbo.supervisor_requests'))
BEGIN
    ALTER TABLE dbo.supervisor_requests DROP CONSTRAINT ck_supervisor_requests_status;
END;
ALTER TABLE dbo.supervisor_requests ADD CONSTRAINT ck_supervisor_requests_status
    CHECK (status IN (N'PENDING', N'ACCEPTED', N'REJECTED', N'CANCELLED', N'EXPIRED', N'RELEASED'));
COMMIT;
GO
