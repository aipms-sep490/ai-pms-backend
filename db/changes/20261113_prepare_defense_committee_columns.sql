-- Separate batch before 20261114_confirm_defense_committees.sql.
-- SQL Server binds filtered-index/check expressions before an ALTER COLUMN in the same batch.
-- This additive prerequisite preserves the checksum of the already published 20261114 migration.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.defense_committees', N'U') IS NULL
    THROW 51000, 'Apply the defense committee schema before this change.', 1;

IF COL_LENGTH(N'dbo.defense_committees', N'status') IS NULL
    ALTER TABLE dbo.defense_committees ADD status varchar(20) NOT NULL
        CONSTRAINT df_defense_committee_status DEFAULT 'DRAFT';
IF COL_LENGTH(N'dbo.defense_committees', N'version') IS NULL
    ALTER TABLE dbo.defense_committees ADD version int NOT NULL
        CONSTRAINT df_defense_committee_version DEFAULT 1;

COMMIT;
GO
