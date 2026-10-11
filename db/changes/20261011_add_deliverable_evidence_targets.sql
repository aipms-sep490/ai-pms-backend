SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.deliverables','evidence_scope') IS NULL
    ALTER TABLE dbo.deliverables ADD evidence_scope varchar(20) NULL;
IF COL_LENGTH('dbo.deliverables','target_major_id') IS NULL
    ALTER TABLE dbo.deliverables ADD target_major_id bigint NULL;
IF COL_LENGTH('dbo.deliverables','target_student_id') IS NULL
    ALTER TABLE dbo.deliverables ADD target_student_id bigint NULL;
GO
-- NULL is deliberately legacy/unknown; historical targets are never inferred.
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_deliverables_evidence_target')
    ALTER TABLE dbo.deliverables WITH CHECK ADD CONSTRAINT ck_deliverables_evidence_target CHECK (
        (evidence_scope IS NULL AND target_major_id IS NULL AND target_student_id IS NULL)
        OR (evidence_scope IS NOT NULL AND (
            (evidence_scope='COMMON' AND target_major_id IS NULL AND target_student_id IS NULL)
            OR (evidence_scope='MAJOR_SPECIFIC' AND target_major_id IS NOT NULL AND target_student_id IS NULL)
            OR (evidence_scope='INDIVIDUAL' AND target_major_id IS NOT NULL AND target_student_id IS NOT NULL))));
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='fk_deliverables_target_major')
    ALTER TABLE dbo.deliverables WITH CHECK ADD CONSTRAINT fk_deliverables_target_major FOREIGN KEY(target_major_id) REFERENCES dbo.majors(id);
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='fk_deliverables_target_student')
    ALTER TABLE dbo.deliverables WITH CHECK ADD CONSTRAINT fk_deliverables_target_student FOREIGN KEY(target_student_id) REFERENCES dbo.users(id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.deliverables') AND name='ix_deliverables_evidence_target')
    CREATE INDEX ix_deliverables_evidence_target ON dbo.deliverables(project_id,evidence_scope,target_major_id,target_student_id);
COMMIT;
GO
