-- PR-P0-C (P0.4, P0.6, P0.7, P0.8):
-- Scan lifecycle columns on dbo.files and dbo.review_gate_evidence,
-- immutable GatePackageSnapshot tables (review_gate_packages, review_gate_package_items),
-- and package_id pinning on review_gate_evidence_verifications and review_gate_decisions.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.files', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.files', N'scan_status') IS NULL
        ALTER TABLE dbo.files ADD scan_status varchar(20) NOT NULL CONSTRAINT df_files_scan_status DEFAULT 'READY';
    IF COL_LENGTH(N'dbo.files', N'scan_provider') IS NULL
        ALTER TABLE dbo.files ADD scan_provider varchar(50) NULL;
    IF COL_LENGTH(N'dbo.files', N'scanned_at') IS NULL
        ALTER TABLE dbo.files ADD scanned_at datetime2(0) NULL;
    IF COL_LENGTH(N'dbo.files', N'scan_details') IS NULL
        ALTER TABLE dbo.files ADD scan_details nvarchar(1000) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_files_scan_status' AND parent_object_id = OBJECT_ID(N'dbo.files'))
        EXEC(N'ALTER TABLE dbo.files ADD CONSTRAINT ck_files_scan_status CHECK (scan_status IN (''UPLOADING'',''SCANNING'',''READY'',''QUARANTINED'',''PREVIEW_FAILED''));');
END;

IF OBJECT_ID(N'dbo.review_gate_evidence', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.review_gate_evidence', N'scan_status') IS NULL
        ALTER TABLE dbo.review_gate_evidence ADD scan_status varchar(20) NOT NULL CONSTRAINT df_review_gate_evidence_scan_status DEFAULT 'READY';
    IF COL_LENGTH(N'dbo.review_gate_evidence', N'scan_provider') IS NULL
        ALTER TABLE dbo.review_gate_evidence ADD scan_provider varchar(50) NULL;
    IF COL_LENGTH(N'dbo.review_gate_evidence', N'scanned_at') IS NULL
        ALTER TABLE dbo.review_gate_evidence ADD scanned_at datetime2(0) NULL;
    IF COL_LENGTH(N'dbo.review_gate_evidence', N'scan_details') IS NULL
        ALTER TABLE dbo.review_gate_evidence ADD scan_details nvarchar(1000) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_review_gate_evidence_scan_status' AND parent_object_id = OBJECT_ID(N'dbo.review_gate_evidence'))
        EXEC(N'ALTER TABLE dbo.review_gate_evidence ADD CONSTRAINT ck_review_gate_evidence_scan_status CHECK (scan_status IN (''UPLOADING'',''SCANNING'',''READY'',''QUARANTINED'',''PREVIEW_FAILED''));');
END;

IF OBJECT_ID(N'dbo.review_gate_packages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.review_gate_packages (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_review_gate_packages PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NOT NULL,
        round_number int NOT NULL,
        idempotency_key varchar(128) NOT NULL,
        request_hash varchar(64) NOT NULL,
        notes nvarchar(2000) NULL,
        submitted_by bigint NOT NULL,
        submitted_at datetime2(0) NOT NULL,
        CONSTRAINT uq_review_gate_packages_idempotency UNIQUE (gate_instance_id, idempotency_key),
        CONSTRAINT uq_review_gate_packages_round UNIQUE (gate_instance_id, round_number),
        CONSTRAINT fk_review_gate_packages_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_review_gate_packages_gate FOREIGN KEY (gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT fk_review_gate_packages_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_review_gate_packages_round CHECK (round_number > 0)
    );
    CREATE INDEX ix_review_gate_packages_gate ON dbo.review_gate_packages(gate_instance_id, round_number DESC);
END;

IF OBJECT_ID(N'dbo.review_gate_package_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.review_gate_package_items (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_review_gate_package_items PRIMARY KEY,
        package_id bigint NOT NULL,
        requirement_id bigint NOT NULL,
        evidence_id bigint NOT NULL,
        evidence_version int NOT NULL,
        sha256 varchar(64) NOT NULL,
        CONSTRAINT uq_review_gate_package_items_req UNIQUE (package_id, requirement_id),
        CONSTRAINT fk_review_gate_pkg_items_pkg FOREIGN KEY (package_id) REFERENCES dbo.review_gate_packages(id),
        CONSTRAINT fk_review_gate_pkg_items_req FOREIGN KEY (requirement_id) REFERENCES dbo.project_gate_requirements(id),
        CONSTRAINT fk_review_gate_pkg_items_ev FOREIGN KEY (evidence_id) REFERENCES dbo.review_gate_evidence(id),
        CONSTRAINT ck_review_gate_pkg_items_ver CHECK (evidence_version > 0)
    );
    CREATE INDEX ix_review_gate_package_items_evidence ON dbo.review_gate_package_items(evidence_id);
END;

IF OBJECT_ID(N'dbo.review_gate_evidence_verifications', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.review_gate_evidence_verifications', N'package_id') IS NULL
        ALTER TABLE dbo.review_gate_evidence_verifications ADD package_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_gate_ev_verifications_package')
        EXEC(N'ALTER TABLE dbo.review_gate_evidence_verifications ADD CONSTRAINT fk_gate_ev_verifications_package FOREIGN KEY (package_id) REFERENCES dbo.review_gate_packages(id);');
END;

IF OBJECT_ID(N'dbo.review_gate_decisions', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.review_gate_decisions', N'package_id') IS NULL
        ALTER TABLE dbo.review_gate_decisions ADD package_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_gate_decisions_package')
        EXEC(N'ALTER TABLE dbo.review_gate_decisions ADD CONSTRAINT fk_gate_decisions_package FOREIGN KEY (package_id) REFERENCES dbo.review_gate_packages(id);');
END;

IF OBJECT_ID(N'dbo.cold_submission_files', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_cold_file_size'
          AND parent_object_id = OBJECT_ID(N'dbo.cold_submission_files')
          AND definition LIKE N'%10485760)%'
    )
    BEGIN
        ALTER TABLE dbo.cold_submission_files DROP CONSTRAINT ck_cold_file_size;
    END;
    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_cold_file_size'
          AND parent_object_id = OBJECT_ID(N'dbo.cold_submission_files')
    )
    BEGIN
        ALTER TABLE dbo.cold_submission_files ADD CONSTRAINT ck_cold_file_size CHECK(size_bytes>0 AND size_bytes<=104857600);
    END;
END;

COMMIT TRANSACTION;
GO
