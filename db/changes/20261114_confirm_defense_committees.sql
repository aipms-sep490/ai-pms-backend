SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.defense_committees', N'U') IS NULL
    THROW 51000, 'Apply the defense committee schema before this change.', 1;

IF COL_LENGTH(N'dbo.defense_committees', N'status') IS NULL
    ALTER TABLE dbo.defense_committees ADD status varchar(20) NOT NULL
        CONSTRAINT df_defense_committee_status DEFAULT 'DRAFT';
IF COL_LENGTH(N'dbo.defense_committees', N'version') IS NULL
    ALTER TABLE dbo.defense_committees ADD version int NOT NULL
        CONSTRAINT df_defense_committee_version DEFAULT 1;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.defense_committees')
               AND name = N'ck_defense_committee_status')
    ALTER TABLE dbo.defense_committees WITH CHECK ADD CONSTRAINT ck_defense_committee_status
        CHECK (status IN ('DRAFT','CONFIRMED','SUPERSEDED'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.defense_committees')
               AND name = N'ux_defense_committee_confirmed')
    CREATE UNIQUE INDEX ux_defense_committee_confirmed ON dbo.defense_committees(project_id)
        WHERE status = 'CONFIRMED';

COMMIT;
GO
