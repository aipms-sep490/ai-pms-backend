-- BE-16 result policy and publication; additive/rerunnable, no historical backfill.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.evaluation_finalizations', N'U') IS NULL
        THROW 51000, 'Apply evaluation finalizations first.', 1;
    IF OBJECT_ID(N'dbo.project_result_policies', N'U') IS NULL
        CREATE TABLE dbo.project_result_policies (
            project_id BIGINT NOT NULL CONSTRAINT pk_project_result_policies PRIMARY KEY,
            pass_threshold DECIMAL(4,2) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            updated_by BIGINT NOT NULL,
            updated_at DATETIME2(0) NOT NULL,
            CONSTRAINT fk_result_policy_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_result_policy_actor FOREIGN KEY (updated_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_result_policy_threshold CHECK (pass_threshold BETWEEN 0 AND 10)
        );
    IF OBJECT_ID(N'dbo.project_result_policy_items', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_result_policy_items (
            project_id BIGINT NOT NULL, assignment_id BIGINT NOT NULL, weight_percent DECIMAL(5,2) NOT NULL,
            CONSTRAINT pk_project_result_policy_items PRIMARY KEY (project_id, assignment_id),
            CONSTRAINT fk_result_policy_items_policy FOREIGN KEY (project_id) REFERENCES dbo.project_result_policies(project_id),
            CONSTRAINT fk_result_policy_items_assignment FOREIGN KEY (assignment_id) REFERENCES dbo.evaluation_assignments(id),
            CONSTRAINT ck_result_policy_items_weight CHECK (weight_percent > 0 AND weight_percent <= 100)
        );
        CREATE INDEX ix_result_policy_items_assignment ON dbo.project_result_policy_items(assignment_id);
    END;
    IF OBJECT_ID(N'dbo.project_results', N'U') IS NULL
        CREATE TABLE dbo.project_results (
            id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_results PRIMARY KEY,
            project_id BIGINT NOT NULL CONSTRAINT uq_project_results_project UNIQUE,
            final_submission_id BIGINT NOT NULL, published_by BIGINT NOT NULL, published_at DATETIME2(0) NOT NULL,
            snapshot_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT fk_project_results_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
            CONSTRAINT fk_project_results_submission FOREIGN KEY (final_submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_project_results_actor FOREIGN KEY (published_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_project_results_snapshot CHECK (ISJSON(snapshot_json) = 1)
        );
    IF OBJECT_ID(N'dbo.project_result_evaluations', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_result_evaluations (
            result_id BIGINT NOT NULL, evaluation_id BIGINT NOT NULL,
            CONSTRAINT pk_project_result_evaluations PRIMARY KEY (result_id, evaluation_id),
            CONSTRAINT fk_result_evaluations_result FOREIGN KEY (result_id) REFERENCES dbo.project_results(id),
            CONSTRAINT fk_result_evaluations_finalization FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluation_finalizations(evaluation_id)
        );
        CREATE INDEX ix_project_result_evaluations_evaluation ON dbo.project_result_evaluations(evaluation_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
