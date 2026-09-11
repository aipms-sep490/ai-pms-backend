-- Prerequisite: schema.sql and 20260911_add_rubric_versions.sql.
-- Additive draft-evaluation foundation. Does not infer assignments for legacy evaluations.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.rubric_versions', N'U') IS NULL
        THROW 51000, 'Apply the rubric version migration first.', 1;

    IF OBJECT_ID(N'dbo.evaluation_assignments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_assignments (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_evaluation_assignments PRIMARY KEY,
            project_id BIGINT NOT NULL,
            evaluator_id BIGINT NOT NULL,
            rubric_id BIGINT NOT NULL,
            project_period_id BIGINT NOT NULL,
            department_id BIGINT NOT NULL,
            evaluation_type NVARCHAR(30) NOT NULL,
            status NVARCHAR(20) NOT NULL,
            assigned_by BIGINT NOT NULL,
            assigned_at DATETIME2(0) NOT NULL,
            revoked_at DATETIME2(0) NULL,
            revocation_reason NVARCHAR(1000) NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT fk_evaluation_assignments_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_evaluation_assignments_evaluator FOREIGN KEY (evaluator_id) REFERENCES dbo.users(id),
            CONSTRAINT fk_evaluation_assignments_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT fk_evaluation_assignments_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_evaluation_assignments_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
            CONSTRAINT fk_evaluation_assignments_actor FOREIGN KEY (assigned_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_evaluation_assignments_type CHECK (evaluation_type IN (N'SUPERVISOR',N'LECTURER')),
            CONSTRAINT ck_evaluation_assignments_state CHECK (
                (status = N'ACTIVE' AND revoked_at IS NULL AND revocation_reason IS NULL)
                OR (status = N'REVOKED' AND revoked_at >= assigned_at AND LEN(LTRIM(RTRIM(revocation_reason))) > 0 AND revoked_at IS NOT NULL AND revocation_reason IS NOT NULL))
        );
        CREATE UNIQUE INDEX uq_evaluation_assignments_active ON dbo.evaluation_assignments(project_id,evaluator_id,evaluation_type) WHERE status = N'ACTIVE';
        CREATE INDEX ix_evaluation_assignments_evaluator ON dbo.evaluation_assignments(evaluator_id,status,id);
    END;

    IF OBJECT_ID(N'dbo.evaluation_draft_states', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_draft_states (
            evaluation_id BIGINT NOT NULL CONSTRAINT pk_evaluation_draft_states PRIMARY KEY,
            assignment_id BIGINT NOT NULL CONSTRAINT uq_evaluation_draft_states_assignment UNIQUE,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            calculation_rule VARCHAR(50) NOT NULL,
            CONSTRAINT fk_evaluation_draft_states_evaluation FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluations(id),
            CONSTRAINT fk_evaluation_draft_states_assignment FOREIGN KEY (assignment_id) REFERENCES dbo.evaluation_assignments(id),
            CONSTRAINT ck_evaluation_draft_states_rule CHECK (calculation_rule = 'WEIGHTED_10_AWAY_FROM_ZERO_2DP_V1')
        );
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
