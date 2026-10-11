SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.evaluation_scheme_components','stage') IS NULL
 ALTER TABLE dbo.evaluation_scheme_components ADD stage varchar(20) NULL;
IF COL_LENGTH('dbo.evaluation_assignments','stage') IS NULL
 ALTER TABLE dbo.evaluation_assignments ADD stage varchar(20) NULL;
IF COL_LENGTH('dbo.evaluation_assignments','cold_package_id') IS NULL
 ALTER TABLE dbo.evaluation_assignments ADD cold_package_id bigint NULL;
GO
IF OBJECT_ID('dbo.ck_scheme_component_stage','C') IS NULL
 ALTER TABLE dbo.evaluation_scheme_components WITH CHECK ADD CONSTRAINT ck_scheme_component_stage
 CHECK(stage IS NULL OR stage IN ('ON_GOING','COLD','DEFENSE'));
IF OBJECT_ID('dbo.ck_assignment_stage','C') IS NULL
 ALTER TABLE dbo.evaluation_assignments WITH CHECK ADD CONSTRAINT ck_assignment_stage
 CHECK((stage IS NULL AND cold_package_id IS NULL) OR
 (stage IS NOT NULL AND stage IN ('ON_GOING','DEFENSE') AND cold_package_id IS NULL) OR (stage IS NOT NULL AND stage='COLD' AND cold_package_id IS NOT NULL));
IF OBJECT_ID('dbo.fk_assignment_cold_package','F') IS NULL
 ALTER TABLE dbo.evaluation_assignments WITH CHECK ADD CONSTRAINT fk_assignment_cold_package
 FOREIGN KEY(cold_package_id) REFERENCES dbo.cold_packages(id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.evaluation_assignments') AND name='ix_assignment_cold_package')
 CREATE INDEX ix_assignment_cold_package ON dbo.evaluation_assignments(cold_package_id) WHERE cold_package_id IS NOT NULL;
IF OBJECT_ID('dbo.ck_evaluation_assignments_type','C') IS NOT NULL
 ALTER TABLE dbo.evaluation_assignments DROP CONSTRAINT ck_evaluation_assignments_type;
ALTER TABLE dbo.evaluation_assignments WITH CHECK ADD CONSTRAINT ck_evaluation_assignments_type
 CHECK(evaluation_type IN (N'SUPERVISOR',N'LECTURER',N'COMMITTEE'));
COMMIT;
GO
