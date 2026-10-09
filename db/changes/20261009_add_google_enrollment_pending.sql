SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.users', 'google_enrollment_pending') IS NULL
    ALTER TABLE dbo.users ADD google_enrollment_pending BIT NOT NULL
        CONSTRAINT df_users_google_enrollment_pending DEFAULT (0) WITH VALUES;
COMMIT;
