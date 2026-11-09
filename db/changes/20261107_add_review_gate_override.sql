SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.project_gate_instances', 'decision_source') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD decision_source varchar(20) NOT NULL CONSTRAINT DF_project_gate_decision_source DEFAULT 'REVIEW';
IF COL_LENGTH('dbo.project_gate_instances', 'overridden_by') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD overridden_by bigint NULL;
IF COL_LENGTH('dbo.project_gate_instances', 'overridden_at') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD overridden_at datetime2(0) NULL;
IF COL_LENGTH('dbo.project_gate_instances', 'override_reason') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD override_reason nvarchar(2000) NULL;

COMMIT TRANSACTION;
