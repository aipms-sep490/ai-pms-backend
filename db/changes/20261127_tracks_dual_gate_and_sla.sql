-- PR-P1-A (P1.1 / POL-02, POL-04, POL-05):
-- Explicit 3 academic tracks (IT_ONLY, MKT_ONLY, IT_MKT), POL-04/POL-05 fields,
-- per-Gate/round dual-department scope decisions (LeadScope + ParticipatingScope),
-- and 48h/72h SLA reminder/escalation deduplication ledger.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.team_academic_configurations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.team_academic_configurations', N'track_code') IS NULL
        ALTER TABLE dbo.team_academic_configurations ADD track_code varchar(20) NULL;
    IF COL_LENGTH(N'dbo.team_academic_configurations', N'has_integration_deliverable') IS NULL
        ALTER TABLE dbo.team_academic_configurations ADD has_integration_deliverable bit NOT NULL CONSTRAINT df_team_cfg_has_int DEFAULT 0;
    IF COL_LENGTH(N'dbo.team_academic_configurations', N'mkt_attribution_limits_note') IS NULL
        ALTER TABLE dbo.team_academic_configurations ADD mkt_attribution_limits_note nvarchar(2000) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_team_academic_track' AND parent_object_id = OBJECT_ID(N'dbo.team_academic_configurations'))
        EXEC(N'ALTER TABLE dbo.team_academic_configurations ADD CONSTRAINT ck_team_academic_track CHECK (track_code IS NULL OR track_code IN (''IT_ONLY'',''MKT_ONLY'',''IT_MKT''));');
END;

IF OBJECT_ID(N'dbo.projects', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.projects', N'track_code') IS NULL
        ALTER TABLE dbo.projects ADD track_code varchar(20) NULL;
    IF COL_LENGTH(N'dbo.projects', N'integration_deliverable_summary') IS NULL
        ALTER TABLE dbo.projects ADD integration_deliverable_summary nvarchar(2000) NULL;
    IF COL_LENGTH(N'dbo.projects', N'mkt_attribution_limits') IS NULL
        ALTER TABLE dbo.projects ADD mkt_attribution_limits nvarchar(2000) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_projects_track_code' AND parent_object_id = OBJECT_ID(N'dbo.projects'))
        EXEC(N'ALTER TABLE dbo.projects ADD CONSTRAINT ck_projects_track_code CHECK (track_code IS NULL OR track_code IN (''IT_ONLY'',''MKT_ONLY'',''IT_MKT''));');
END;

IF OBJECT_ID(N'dbo.project_gate_round_scope_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_gate_round_scope_decisions (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_gate_round_scope_decisions PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NOT NULL,
        round_number int NOT NULL,
        scope varchar(30) NOT NULL,
        department_id bigint NULL,
        major_id bigint NULL,
        decision varchar(30) NOT NULL CONSTRAINT df_gate_round_scope_decision DEFAULT 'PENDING',
        notes nvarchar(2000) NULL,
        decided_by bigint NULL,
        decided_at datetime2(0) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_gate_round_scope_token DEFAULT NEWID(),
        CONSTRAINT uq_gate_round_scope UNIQUE (gate_instance_id, round_number, scope),
        CONSTRAINT fk_gate_round_scope_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_gate_round_scope_gate FOREIGN KEY (gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT fk_gate_round_scope_dept FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_gate_round_scope_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_gate_round_scope_actor FOREIGN KEY (decided_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_gate_round_scope_round CHECK (round_number > 0),
        CONSTRAINT ck_gate_round_scope_decision CHECK (decision IN ('PENDING','PASSED','REVISION_REQUIRED','OVERRIDDEN_PASS'))
    );
    CREATE INDEX ix_gate_round_scope_project ON dbo.project_gate_round_scope_decisions(project_id, gate_instance_id, round_number);
END;

IF OBJECT_ID(N'dbo.workflow_sla_notifications', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.workflow_sla_notifications (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_workflow_sla_notifications PRIMARY KEY,
        entity_type varchar(40) NOT NULL,
        entity_id bigint NOT NULL,
        round_number int NOT NULL CONSTRAINT df_workflow_sla_round DEFAULT 1,
        department_id bigint NOT NULL,
        sla_tier varchar(20) NOT NULL,
        sent_at datetime2(0) NOT NULL,
        CONSTRAINT uq_workflow_sla_dedup UNIQUE (entity_type, entity_id, round_number, department_id, sla_tier),
        CONSTRAINT fk_workflow_sla_dept FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT ck_workflow_sla_entity CHECK (entity_type IN ('PROJECT_REVIEW','REVIEW_GATE')),
        CONSTRAINT ck_workflow_sla_tier CHECK (sla_tier IN ('REMINDER_48H','ESCALATION_72H'))
    );
    CREATE INDEX ix_workflow_sla_lookup ON dbo.workflow_sla_notifications(entity_type, entity_id, sla_tier);
END;

COMMIT TRANSACTION;
GO
