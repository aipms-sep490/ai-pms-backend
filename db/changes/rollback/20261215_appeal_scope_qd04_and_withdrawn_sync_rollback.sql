-- Rollback for 20261215_appeal_scope_qd04_and_withdrawn_sync.sql
SET XACT_ABORT ON;
GO
IF OBJECT_ID(N'dbo.student_defense_absences', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences') AND name = N'ck_student_defense_absence_revocation')
        ALTER TABLE dbo.student_defense_absences DROP CONSTRAINT ck_student_defense_absence_revocation;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences') AND name = N'fk_student_defense_absence_appeal')
        ALTER TABLE dbo.student_defense_absences DROP CONSTRAINT fk_student_defense_absence_appeal;
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences') AND name = N'fk_student_defense_absence_revoker')
        ALTER TABLE dbo.student_defense_absences DROP CONSTRAINT fk_student_defense_absence_revoker;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.student_defense_absences') AND name = N'ux_student_defense_absence_active')
        DROP INDEX ux_student_defense_absence_active ON dbo.student_defense_absences;
    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_by_appeal_id') IS NOT NULL
        ALTER TABLE dbo.student_defense_absences DROP COLUMN revoked_by_appeal_id;
    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_reason') IS NOT NULL
        ALTER TABLE dbo.student_defense_absences DROP COLUMN revoked_reason;
    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_at') IS NOT NULL
        ALTER TABLE dbo.student_defense_absences DROP COLUMN revoked_at;
    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_by') IS NOT NULL
        ALTER TABLE dbo.student_defense_absences DROP COLUMN revoked_by;
    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences') AND name = N'uq_student_defense_absence')
        ALTER TABLE dbo.student_defense_absences ADD CONSTRAINT uq_student_defense_absence UNIQUE (project_id, student_id);
END;
GO
IF OBJECT_ID(N'dbo.result_gate_score_amendments', N'U') IS NOT NULL
    DROP TABLE dbo.result_gate_score_amendments;
GO
IF OBJECT_ID(N'dbo.result_correction_bundles', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.result_correction_bundles') AND name = N'ck_correction_bundle_absence_override')
        ALTER TABLE dbo.result_correction_bundles DROP CONSTRAINT ck_correction_bundle_absence_override;
    IF COL_LENGTH(N'dbo.result_correction_bundles', N'absence_override_json') IS NOT NULL
        ALTER TABLE dbo.result_correction_bundles DROP COLUMN absence_override_json;
END;
GO
IF OBJECT_ID(N'dbo.result_appeal_requests', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests') AND name = N'fk_appeal_request_supervisor_responder')
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT fk_appeal_request_supervisor_responder;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests') AND name = N'ck_appeal_request_component_ids')
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT ck_appeal_request_component_ids;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests') AND name = N'ck_appeal_request_kind')
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT ck_appeal_request_kind;
    IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests') AND name = N'df_appeal_request_kind')
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT df_appeal_request_kind;
    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_responded_at') IS NOT NULL
        ALTER TABLE dbo.result_appeal_requests DROP COLUMN supervisor_responded_at;
    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_responded_by') IS NOT NULL
        ALTER TABLE dbo.result_appeal_requests DROP COLUMN supervisor_responded_by;
    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_note') IS NOT NULL
        ALTER TABLE dbo.result_appeal_requests DROP COLUMN supervisor_note;
    IF COL_LENGTH(N'dbo.result_appeal_requests', N'component_ids_json') IS NOT NULL
        ALTER TABLE dbo.result_appeal_requests DROP COLUMN component_ids_json;
    IF COL_LENGTH(N'dbo.result_appeal_requests', N'appeal_kind') IS NOT NULL
        ALTER TABLE dbo.result_appeal_requests DROP COLUMN appeal_kind;
END;
GO
