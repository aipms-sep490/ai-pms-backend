-- TK-W1-17: Proposal POL-02 Consolidated Revision & Lead Override (3 Working Days)
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.project_department_decisions', 'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_project_decision_state' AND parent_object_id = OBJECT_ID(N'dbo.project_department_decisions'))
        ALTER TABLE dbo.project_department_decisions DROP CONSTRAINT ck_project_decision_state;

    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_project_decision_reason' AND parent_object_id = OBJECT_ID(N'dbo.project_department_decisions'))
        ALTER TABLE dbo.project_department_decisions DROP CONSTRAINT ck_project_decision_reason;

    ALTER TABLE dbo.project_department_decisions
        ADD CONSTRAINT ck_project_decision_state CHECK (
            (decision = 'PENDING' AND decided_by IS NULL AND decided_at IS NULL)
            OR (decision IN ('APPROVED', 'REVISION_REQUIRED', 'REJECTED') AND decided_by IS NOT NULL AND decided_at IS NOT NULL));

    ALTER TABLE dbo.project_department_decisions
        ADD CONSTRAINT ck_project_decision_reason CHECK (
            decision NOT IN ('REJECTED', 'REVISION_REQUIRED')
            OR (reason IS NOT NULL AND LEN(LTRIM(RTRIM(reason))) > 0));
END;

IF OBJECT_ID('dbo.project_registration_snapshots', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('dbo.project_registration_snapshots', 'is_lead_override') IS NULL
        ALTER TABLE dbo.project_registration_snapshots
            ADD is_lead_override BIT NOT NULL CONSTRAINT df_prs_lead_override DEFAULT (0);

    IF COL_LENGTH('dbo.project_registration_snapshots', 'override_reason') IS NULL
        ALTER TABLE dbo.project_registration_snapshots
            ADD override_reason NVARCHAR(2000) NULL;

    IF COL_LENGTH('dbo.project_registration_snapshots', 'overridden_by') IS NULL
        ALTER TABLE dbo.project_registration_snapshots
            ADD overridden_by BIGINT NULL;

    IF COL_LENGTH('dbo.project_registration_snapshots', 'overridden_at') IS NULL
        ALTER TABLE dbo.project_registration_snapshots
            ADD overridden_at DATETIME2(0) NULL;

    IF COL_LENGTH('dbo.project_registration_snapshots', 'consolidated_revision_notes') IS NULL
        ALTER TABLE dbo.project_registration_snapshots
            ADD consolidated_revision_notes NVARCHAR(MAX) NULL;
END;

COMMIT TRANSACTION;
GO

IF OBJECT_ID('dbo.project_registration_snapshots', 'U') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_prs_overridden_by')
BEGIN
    ALTER TABLE dbo.project_registration_snapshots
        ADD CONSTRAINT fk_prs_overridden_by FOREIGN KEY (overridden_by) REFERENCES dbo.users(id);
END;
GO
