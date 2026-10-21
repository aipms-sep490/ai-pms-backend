SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.review_gate_definitions', 'weight') IS NULL
    ALTER TABLE dbo.review_gate_definitions ADD weight decimal(5,2) NULL;

IF COL_LENGTH('dbo.project_gate_instances', 'weight') IS NULL
    ALTER TABLE dbo.project_gate_instances ADD weight decimal(5,2) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_review_gate_definition_weight')
    EXEC(N'ALTER TABLE dbo.review_gate_definitions ADD CONSTRAINT ck_review_gate_definition_weight CHECK (weight IS NULL OR (weight >= 0 AND weight <= 100))');

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_project_gate_instance_weight')
    EXEC(N'ALTER TABLE dbo.project_gate_instances ADD CONSTRAINT ck_project_gate_instance_weight CHECK (weight IS NULL OR (weight >= 0 AND weight <= 100))');

COMMIT;
