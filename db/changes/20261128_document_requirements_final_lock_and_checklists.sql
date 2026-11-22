SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. 9-attribute Document Requirement Contract on review_gate_requirements & project_gate_requirements (Spec V4.0 §6.4.2)
IF COL_LENGTH(N'dbo.review_gate_requirements', N'target_cardinality') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD target_cardinality varchar(20) NOT NULL CONSTRAINT df_rgr_target_cardinality DEFAULT 'PROJECT';
IF COL_LENGTH(N'dbo.review_gate_requirements', N'delivery_stage') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD delivery_stage varchar(20) NULL;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'feeds_cold') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD feeds_cold bit NOT NULL CONSTRAINT df_rgr_feeds_cold DEFAULT 0;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'feeds_defense') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD feeds_defense bit NOT NULL CONSTRAINT df_rgr_feeds_defense DEFAULT 0;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'input_kind') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD input_kind varchar(30) NULL;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'template_document_id') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD template_document_id bigint NULL;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'carry_forward_from_code') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD carry_forward_from_code varchar(50) NULL;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'policy_tag') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD policy_tag varchar(60) NULL;
IF COL_LENGTH(N'dbo.review_gate_requirements', N'rubric_criterion_ref') IS NULL
    ALTER TABLE dbo.review_gate_requirements ADD rubric_criterion_ref varchar(50) NULL;

IF COL_LENGTH(N'dbo.project_gate_requirements', N'target_cardinality') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD target_cardinality varchar(20) NOT NULL CONSTRAINT df_pgr_target_cardinality DEFAULT 'PROJECT';
IF COL_LENGTH(N'dbo.project_gate_requirements', N'delivery_stage') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD delivery_stage varchar(20) NULL;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'feeds_cold') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD feeds_cold bit NOT NULL CONSTRAINT df_pgr_feeds_cold DEFAULT 0;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'feeds_defense') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD feeds_defense bit NOT NULL CONSTRAINT df_pgr_feeds_defense DEFAULT 0;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'input_kind') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD input_kind varchar(30) NULL;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'template_document_id') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD template_document_id bigint NULL;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'carry_forward_from_code') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD carry_forward_from_code varchar(50) NULL;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'policy_tag') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD policy_tag varchar(60) NULL;
IF COL_LENGTH(N'dbo.project_gate_requirements', N'rubric_criterion_ref') IS NULL
    ALTER TABLE dbo.project_gate_requirements ADD rubric_criterion_ref varchar(50) NULL;

-- 2. COMMON / INTEGRATION scope support on department_document_templates
IF COL_LENGTH(N'dbo.department_document_templates', N'scope') IS NULL
    ALTER TABLE dbo.department_document_templates ADD scope varchar(30) NOT NULL CONSTRAINT df_dept_doc_tpl_scope DEFAULT 'MAJOR_SPECIFIC';

-- 3. 2-Step Final Lock workflow (PENDING_SUPERVISOR_LOCK -> LOCKED + Lead Force-Lock + Cold manifest SHA-256)
IF COL_LENGTH(N'dbo.final_submission_drafts', N'lock_workflow_status') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD lock_workflow_status varchar(30) NOT NULL CONSTRAINT df_fsd_lock_workflow_status DEFAULT 'OPEN';
IF COL_LENGTH(N'dbo.final_submission_drafts', N'lock_requested_by') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD lock_requested_by bigint NULL;
IF COL_LENGTH(N'dbo.final_submission_drafts', N'lock_requested_at') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD lock_requested_at datetime2(0) NULL;
IF COL_LENGTH(N'dbo.final_submission_drafts', N'locked_by_supervisor_id') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD locked_by_supervisor_id bigint NULL;
IF COL_LENGTH(N'dbo.final_submission_drafts', N'force_locked_by') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD force_locked_by bigint NULL;
IF COL_LENGTH(N'dbo.final_submission_drafts', N'force_lock_reason') IS NULL
    ALTER TABLE dbo.final_submission_drafts ADD force_lock_reason nvarchar(1000) NULL;

IF COL_LENGTH(N'dbo.final_submissions', N'locked_by_supervisor_id') IS NULL
    ALTER TABLE dbo.final_submissions ADD locked_by_supervisor_id bigint NULL;
IF COL_LENGTH(N'dbo.final_submissions', N'force_locked_by') IS NULL
    ALTER TABLE dbo.final_submissions ADD force_locked_by bigint NULL;
IF COL_LENGTH(N'dbo.final_submissions', N'force_lock_reason') IS NULL
    ALTER TABLE dbo.final_submissions ADD force_lock_reason nvarchar(1000) NULL;
IF COL_LENGTH(N'dbo.final_submissions', N'cold_manifest_sha256') IS NULL
    ALTER TABLE dbo.final_submissions ADD cold_manifest_sha256 char(64) NULL;

IF COL_LENGTH(N'dbo.cold_checklist_items', N'waived_status') IS NULL
    ALTER TABLE dbo.cold_checklist_items ADD waived_status varchar(30) NULL;

-- 4. Master Review Checklist Template & Project Tailored Checklist Layer 1 (Spec V4.0 §6.5.1 — Pure Core, AI-Ready Layer 2 columns)
IF OBJECT_ID(N'dbo.review_gate_checklist_templates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.review_gate_checklist_templates (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_review_gate_checklist_templates PRIMARY KEY,
        academic_semester_id bigint NOT NULL,
        track_code varchar(20) NOT NULL,
        code varchar(60) NOT NULL,
        name nvarchar(255) NOT NULL,
        version int NOT NULL CONSTRAINT df_rgct_version DEFAULT 1,
        status varchar(20) NOT NULL CONSTRAINT df_rgct_status DEFAULT 'DRAFT',
        lead_department_id bigint NOT NULL,
        items_json nvarchar(max) NOT NULL CONSTRAINT df_rgct_items DEFAULT N'[]',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_rgct_token DEFAULT NEWID(),
        created_by bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_rgct_created_at DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_rgct_updated_at DEFAULT SYSUTCDATETIME(),
        published_at datetime2(0) NULL,
        CONSTRAINT uq_rgct_code_ver UNIQUE (academic_semester_id, code, version),
        CONSTRAINT fk_rgct_semester FOREIGN KEY (academic_semester_id) REFERENCES dbo.academic_semesters(id),
        CONSTRAINT fk_rgct_dept FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_rgct_creator FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_rgct_track CHECK (track_code IN ('IT_ONLY','MKT_ONLY','IT_MKT')),
        CONSTRAINT ck_rgct_status CHECK (status IN ('DRAFT','PUBLISHED','RETIRED'))
    );
END;

IF OBJECT_ID(N'dbo.project_gate_checklists', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_gate_checklists (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_gate_checklists PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NOT NULL,
        template_id bigint NULL,
        version int NOT NULL CONSTRAINT df_pgc_version DEFAULT 1,
        status varchar(20) NOT NULL CONSTRAINT df_pgc_status DEFAULT 'DRAFT',
        advisor_recommendation varchar(30) NOT NULL CONSTRAINT df_pgc_adv_rec DEFAULT 'PENDING',
        reviewer_recommendation varchar(30) NOT NULL CONSTRAINT df_pgc_rev_rec DEFAULT 'PENDING',
        review3_consensus_status varchar(30) NOT NULL CONSTRAINT df_pgc_r3_consensus DEFAULT 'PENDING_RECOMMENDATIONS',
        published_by bigint NULL,
        published_at datetime2(0) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_pgc_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL CONSTRAINT df_pgc_created_at DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_pgc_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_pgc_gate_ver UNIQUE (gate_instance_id, version),
        CONSTRAINT fk_pgc_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_pgc_gate FOREIGN KEY (gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT fk_pgc_template FOREIGN KEY (template_id) REFERENCES dbo.review_gate_checklist_templates(id),
        CONSTRAINT fk_pgc_publisher FOREIGN KEY (published_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_pgc_status CHECK (status IN ('DRAFT','PUBLISHED')),
        CONSTRAINT ck_pgc_adv_rec CHECK (advisor_recommendation IN ('PENDING','RECOMMEND_DEFENSE','NOT_RECOMMENDED')),
        CONSTRAINT ck_pgc_rev_rec CHECK (reviewer_recommendation IN ('PENDING','RECOMMEND_DEFENSE','NOT_RECOMMENDED')),
        CONSTRAINT ck_pgc_consensus CHECK (review3_consensus_status IN ('PENDING_RECOMMENDATIONS','AGREE','DISAGREE'))
    );
    CREATE INDEX ix_pgc_project_gate ON dbo.project_gate_checklists(project_id, gate_instance_id, version DESC);
END;

IF OBJECT_ID(N'dbo.project_gate_checklist_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_gate_checklist_items (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_gate_checklist_items PRIMARY KEY,
        checklist_id bigint NOT NULL,
        item_code varchar(50) NOT NULL,
        section nvarchar(150) NOT NULL,
        track_scope varchar(20) NOT NULL,
        major_id bigint NULL,
        condition_tag varchar(40) NOT NULL CONSTRAINT df_pgci_cond DEFAULT 'NONE',
        core_question nvarchar(1000) NOT NULL,
        priority varchar(20) NOT NULL,
        weight decimal(5,2) NOT NULL CONSTRAINT df_pgci_weight DEFAULT 1.00,
        is_applicable bit NOT NULL CONSTRAINT df_pgci_app DEFAULT 1,
        na_reason nvarchar(500) NULL,
        tailored_checkpoints nvarchar(2000) NULL,
        tailored_evidence nvarchar(2000) NULL,
        reviewer_focus_prompt nvarchar(2000) NULL,
        source_ref nvarchar(500) NULL,
        advisory_status varchar(30) NULL,
        advisory_notes nvarchar(2000) NULL,
        advisory_by bigint NULL,
        reviewer_score decimal(3,1) NULL,
        reviewer_notes nvarchar(2000) NULL,
        reviewer_by bigint NULL,
        CONSTRAINT uq_pgci_checklist_code UNIQUE (checklist_id, item_code),
        CONSTRAINT fk_pgci_checklist FOREIGN KEY (checklist_id) REFERENCES dbo.project_gate_checklists(id),
        CONSTRAINT fk_pgci_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_pgci_advisory_by FOREIGN KEY (advisory_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_pgci_reviewer_by FOREIGN KEY (reviewer_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_pgci_track_scope CHECK (track_scope IN ('COMMON','IT','MKT','INTEGRATION')),
        CONSTRAINT ck_pgci_priority CHECK (priority IN ('Mandatory','Recommended','Conditional')),
        CONSTRAINT ck_pgci_weight CHECK (weight > 0),
        CONSTRAINT ck_pgci_rev_score CHECK (reviewer_score IS NULL OR reviewer_score IN (0.0, 0.5, 1.0))
    );
END;

COMMIT TRANSACTION;
GO
