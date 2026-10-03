SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.users', 'row_version') IS NULL
    ALTER TABLE dbo.users ADD row_version rowversion NOT NULL;
COMMIT;
GO
