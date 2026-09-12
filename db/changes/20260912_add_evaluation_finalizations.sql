-- BE-09: immutable evaluation evidence/score snapshot. Apply after BE-16 locked packages.
-- Additive and rerunnable; does not finalize or backfill historical grades.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.evaluation_draft_states', N'U') IS NULL
        OR OBJECT_ID(N'dbo.final_submissions', N'U') IS NULL
        THROW 51000, 'Apply evaluation drafts and locked final submissions first.', 1;
    IF OBJECT_ID(N'dbo.evaluation_finalizations', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.evaluation_finalizations (
            evaluation_id BIGINT NOT NULL CONSTRAINT pk_evaluation_finalizations PRIMARY KEY,
            final_submission_id BIGINT NOT NULL,
            finalized_by BIGINT NOT NULL,
            finalized_at DATETIME2(0) NOT NULL,
            snapshot_json NVARCHAR(MAX) NOT NULL,
            CONSTRAINT fk_evaluation_finalizations_evaluation FOREIGN KEY (evaluation_id) REFERENCES dbo.evaluations(id),
            CONSTRAINT fk_evaluation_finalizations_submission FOREIGN KEY (final_submission_id) REFERENCES dbo.final_submissions(id),
            CONSTRAINT fk_evaluation_finalizations_actor FOREIGN KEY (finalized_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_evaluation_finalizations_snapshot CHECK (ISJSON(snapshot_json) = 1)
        );
        CREATE INDEX ix_evaluation_finalizations_submission ON dbo.evaluation_finalizations(final_submission_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
