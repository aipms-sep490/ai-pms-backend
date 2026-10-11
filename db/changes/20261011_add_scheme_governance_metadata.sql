SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.evaluation_schemes','governance_json') IS NULL
    ALTER TABLE dbo.evaluation_schemes ADD governance_json nvarchar(max) NULL;
IF COL_LENGTH('dbo.evaluation_schemes','locked_at') IS NULL
    ALTER TABLE dbo.evaluation_schemes ADD locked_at datetime2(0) NULL;
IF COL_LENGTH('dbo.evaluation_scheme_components','evaluator_weights_json') IS NULL
    ALTER TABLE dbo.evaluation_scheme_components ADD evaluator_weights_json nvarchar(max) NULL;
GO
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_scheme_governance_json')
    ALTER TABLE dbo.evaluation_schemes WITH CHECK ADD CONSTRAINT ck_scheme_governance_json
        CHECK(governance_json IS NULL OR ISJSON(governance_json)=1);
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_component_evaluator_weights_json')
    ALTER TABLE dbo.evaluation_scheme_components WITH CHECK ADD CONSTRAINT ck_component_evaluator_weights_json
        CHECK(evaluator_weights_json IS NULL OR ISJSON(evaluator_weights_json)=1);
-- NULL remains unknown for legacy schemes; no inferred approval or lock timestamp.
COMMIT;
GO
