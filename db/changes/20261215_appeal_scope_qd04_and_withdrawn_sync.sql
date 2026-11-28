-- Wave 4 (TK-W4-02): QD-04 Appeal Scope (80% independent / 20% process errata / DEFENSE_ABSENT contest),
-- combined COLD+DEFENSE componentIds, gate process amendments, absence revocation audit, and WITHDRAWN sync.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- 1. Sync ck_appeal_request_status with WITHDRAWN and add QD-04 columns to dbo.result_appeal_requests
IF OBJECT_ID(N'dbo.result_appeal_requests', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests')
          AND name = N'ck_appeal_request_status'
          AND definition NOT LIKE N'%WITHDRAWN%'
    )
        ALTER TABLE dbo.result_appeal_requests DROP CONSTRAINT ck_appeal_request_status;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests')
          AND name = N'ck_appeal_request_status'
    )
        ALTER TABLE dbo.result_appeal_requests
            ADD CONSTRAINT ck_appeal_request_status
            CHECK (status IN ('PENDING','UNDER_REVIEW','ACCEPTED','REJECTED','WITHDRAWN'));

    IF COL_LENGTH(N'dbo.result_appeal_requests', N'appeal_kind') IS NULL
        ALTER TABLE dbo.result_appeal_requests
            ADD appeal_kind VARCHAR(30) NOT NULL CONSTRAINT df_appeal_request_kind DEFAULT 'INDEPENDENT_80';

    IF COL_LENGTH(N'dbo.result_appeal_requests', N'component_ids_json') IS NULL
        ALTER TABLE dbo.result_appeal_requests ADD component_ids_json NVARCHAR(MAX) NULL;

    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_note') IS NULL
        ALTER TABLE dbo.result_appeal_requests ADD supervisor_note NVARCHAR(2000) NULL;

    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_responded_by') IS NULL
        ALTER TABLE dbo.result_appeal_requests ADD supervisor_responded_by BIGINT NULL;

    IF COL_LENGTH(N'dbo.result_appeal_requests', N'supervisor_responded_at') IS NULL
        ALTER TABLE dbo.result_appeal_requests ADD supervisor_responded_at DATETIME2(0) NULL;
END;
GO

IF OBJECT_ID(N'dbo.result_appeal_requests', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests')
          AND name = N'ck_appeal_request_kind'
    )
        ALTER TABLE dbo.result_appeal_requests
            ADD CONSTRAINT ck_appeal_request_kind
            CHECK (appeal_kind IN ('INDEPENDENT_80','PROCESS_20_ERRATA','DEFENSE_ABSENT_CONTEST'));

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests')
          AND name = N'ck_appeal_request_component_ids'
    )
        ALTER TABLE dbo.result_appeal_requests
            ADD CONSTRAINT ck_appeal_request_component_ids
            CHECK (component_ids_json IS NULL OR ISJSON(component_ids_json) = 1);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_appeal_requests')
          AND name = N'fk_appeal_request_supervisor_responder'
    )
        ALTER TABLE dbo.result_appeal_requests
            ADD CONSTRAINT fk_appeal_request_supervisor_responder
            FOREIGN KEY (supervisor_responded_by) REFERENCES dbo.users(id);
END;
GO

-- 2. Add absence_override_json to dbo.result_correction_bundles and create dbo.result_gate_score_amendments
IF OBJECT_ID(N'dbo.result_correction_bundles', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.result_correction_bundles', N'absence_override_json') IS NULL
        ALTER TABLE dbo.result_correction_bundles ADD absence_override_json NVARCHAR(MAX) NULL;
END;
GO

IF OBJECT_ID(N'dbo.result_correction_bundles', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.result_correction_bundles')
          AND name = N'ck_correction_bundle_absence_override'
    )
        ALTER TABLE dbo.result_correction_bundles
            ADD CONSTRAINT ck_correction_bundle_absence_override
            CHECK (absence_override_json IS NULL OR ISJSON(absence_override_json) = 1);
END;
GO

IF OBJECT_ID(N'dbo.result_gate_score_amendments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.result_gate_score_amendments (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_result_gate_score_amendments PRIMARY KEY,
        bundle_id BIGINT NOT NULL CONSTRAINT fk_gate_amendment_bundle REFERENCES dbo.result_correction_bundles(id),
        gate_score_id BIGINT NOT NULL CONSTRAINT fk_gate_amendment_score REFERENCES dbo.gate_individual_process_scores(id),
        gate_number INT NOT NULL CONSTRAINT ck_gate_amendment_number CHECK (gate_number BETWEEN 1 AND 3),
        score DECIMAL(5,2) NOT NULL CONSTRAINT ck_gate_amendment_score CHECK (score BETWEEN 0 AND 10),
        reason NVARCHAR(2000) NOT NULL,
        CONSTRAINT uq_gate_amendment_bundle_score UNIQUE (bundle_id, gate_score_id)
    );
END;
GO

-- 3. Add audited revocation columns and filtered active unique index on dbo.student_defense_absences
IF OBJECT_ID(N'dbo.student_defense_absences', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_by') IS NULL
        ALTER TABLE dbo.student_defense_absences ADD revoked_by BIGINT NULL;

    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_at') IS NULL
        ALTER TABLE dbo.student_defense_absences ADD revoked_at DATETIME2(0) NULL;

    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_reason') IS NULL
        ALTER TABLE dbo.student_defense_absences ADD revoked_reason NVARCHAR(2000) NULL;

    IF COL_LENGTH(N'dbo.student_defense_absences', N'revoked_by_appeal_id') IS NULL
        ALTER TABLE dbo.student_defense_absences ADD revoked_by_appeal_id BIGINT NULL;
END;
GO

IF OBJECT_ID(N'dbo.student_defense_absences', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences')
          AND name = N'uq_student_defense_absence'
    )
        ALTER TABLE dbo.student_defense_absences DROP CONSTRAINT uq_student_defense_absence;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.student_defense_absences')
          AND name = N'ux_student_defense_absence_active'
    )
        CREATE UNIQUE INDEX ux_student_defense_absence_active
            ON dbo.student_defense_absences(project_id, student_id)
            WHERE revoked_at IS NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences')
          AND name = N'fk_student_defense_absence_revoker'
    )
        ALTER TABLE dbo.student_defense_absences
            ADD CONSTRAINT fk_student_defense_absence_revoker
            FOREIGN KEY (revoked_by) REFERENCES dbo.users(id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences')
          AND name = N'fk_student_defense_absence_appeal'
    )
        ALTER TABLE dbo.student_defense_absences
            ADD CONSTRAINT fk_student_defense_absence_appeal
            FOREIGN KEY (revoked_by_appeal_id) REFERENCES dbo.result_appeal_requests(id);

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.student_defense_absences')
          AND name = N'ck_student_defense_absence_revocation'
    )
        ALTER TABLE dbo.student_defense_absences
            ADD CONSTRAINT ck_student_defense_absence_revocation
            CHECK ((revoked_at IS NULL AND revoked_by IS NULL AND revoked_reason IS NULL)
                OR (revoked_at IS NOT NULL AND revoked_by IS NOT NULL AND LEN(LTRIM(RTRIM(ISNULL(revoked_reason, N'')))) > 0));
END;
GO
