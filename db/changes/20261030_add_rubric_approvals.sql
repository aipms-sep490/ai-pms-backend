-- Migration: 20261030_add_rubric_approvals.sql
-- Description: Expand rubric_versions status to include PENDING_APPROVAL and add rubric_approvals tracking table

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_versions')
           AND name = N'ck_rubric_versions_status')
BEGIN
    ALTER TABLE dbo.rubric_versions DROP CONSTRAINT ck_rubric_versions_status;
END

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.rubric_versions')
               AND name = N'ck_rubric_versions_status')
BEGIN
    EXEC(N'ALTER TABLE dbo.rubric_versions ADD CONSTRAINT ck_rubric_versions_status
           CHECK (status IN (''DRAFT'',''PENDING_APPROVAL'',''PUBLISHED'',''RETIRED''))');
END

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE object_id = OBJECT_ID(N'dbo.rubric_approvals'))
BEGIN
    CREATE TABLE dbo.rubric_approvals (
        id            BIGINT IDENTITY(1,1) NOT NULL,
        rubric_id     BIGINT NOT NULL,
        submitted_by  BIGINT NOT NULL,
        submitted_at  DATETIME2(0) NOT NULL CONSTRAINT df_rubric_approvals_submitted_at DEFAULT (SYSUTCDATETIME()),
        decided_by    BIGINT NULL,
        decided_at    DATETIME2(0) NULL,
        decision      NVARCHAR(16) NULL,
        reason        NVARCHAR(1000) NULL,
        CONSTRAINT pk_rubric_approvals PRIMARY KEY (id),
        CONSTRAINT ck_rubric_approvals_decision CHECK (decision IS NULL OR decision IN ('APPROVED','REJECTED')),
        CONSTRAINT ck_rubric_approvals_decided CHECK ((decided_by IS NULL AND decided_at IS NULL) OR (decided_by IS NOT NULL AND decided_at IS NOT NULL)),
        CONSTRAINT ck_rubric_approvals_four_eyes CHECK (decided_by IS NULL OR decided_by <> submitted_by),
        CONSTRAINT fk_rubric_approvals_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
        CONSTRAINT fk_rubric_approvals_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_rubric_approvals_decider FOREIGN KEY (decided_by) REFERENCES dbo.users(id)
    );
    CREATE INDEX ix_rubric_approvals_rubric ON dbo.rubric_approvals(rubric_id);
END
