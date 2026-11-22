-- PR-P0-B (P0.9, P0.10, POL-01, POL-03, POL-06):
-- TargetScope & IsIntegration on rubric_criteria and evaluation_scheme_components,
-- nullable defense_session_id on evaluation_assignments, enable_defense_room_scheduling on project_periods,
-- outcome_detail on student_results, gate_individual_process_scores (POL-03), and student_defense_absences.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.rubric_criteria', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.rubric_criteria', N'target_scope') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD target_scope varchar(20) NULL;
    IF COL_LENGTH(N'dbo.rubric_criteria', N'is_integration') IS NULL
        ALTER TABLE dbo.rubric_criteria ADD is_integration bit NOT NULL CONSTRAINT df_rubric_criteria_is_integration DEFAULT 0;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_rubric_criteria_target_scope' AND parent_object_id = OBJECT_ID(N'dbo.rubric_criteria'))
        EXEC(N'ALTER TABLE dbo.rubric_criteria ADD CONSTRAINT ck_rubric_criteria_target_scope CHECK (target_scope IS NULL OR target_scope IN (''PROJECT'',''MAJOR'',''STUDENT''));');
END;

IF OBJECT_ID(N'dbo.evaluation_scheme_components', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.evaluation_scheme_components', N'target_scope') IS NULL
        ALTER TABLE dbo.evaluation_scheme_components ADD target_scope varchar(20) NULL;
    IF COL_LENGTH(N'dbo.evaluation_scheme_components', N'is_integration') IS NULL
        ALTER TABLE dbo.evaluation_scheme_components ADD is_integration bit NOT NULL CONSTRAINT df_eval_scheme_comp_is_integration DEFAULT 0;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_eval_scheme_comp_target_scope' AND parent_object_id = OBJECT_ID(N'dbo.evaluation_scheme_components'))
        EXEC(N'ALTER TABLE dbo.evaluation_scheme_components ADD CONSTRAINT ck_eval_scheme_comp_target_scope CHECK (target_scope IS NULL OR target_scope IN (''PROJECT'',''MAJOR'',''STUDENT''));');
END;

IF OBJECT_ID(N'dbo.evaluation_assignments', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.evaluation_assignments', N'defense_session_id') IS NULL
        ALTER TABLE dbo.evaluation_assignments ADD defense_session_id bigint NULL;
    IF OBJECT_ID(N'dbo.defense_sessions', N'U') IS NOT NULL
        AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_evaluation_assignments_defense_session')
        EXEC(N'ALTER TABLE dbo.evaluation_assignments ADD CONSTRAINT fk_evaluation_assignments_defense_session FOREIGN KEY (defense_session_id) REFERENCES dbo.defense_sessions(id);');
END;

IF OBJECT_ID(N'dbo.project_periods', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.project_periods', N'enable_defense_room_scheduling') IS NULL
        ALTER TABLE dbo.project_periods ADD enable_defense_room_scheduling bit NOT NULL CONSTRAINT df_project_periods_defense_sched DEFAULT 0;
END;

IF OBJECT_ID(N'dbo.student_results', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.student_results', N'outcome_detail') IS NULL
        ALTER TABLE dbo.student_results ADD outcome_detail varchar(40) NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_student_results_outcome_detail' AND parent_object_id = OBJECT_ID(N'dbo.student_results'))
        EXEC(N'ALTER TABLE dbo.student_results ADD CONSTRAINT ck_student_results_outcome_detail CHECK (outcome_detail IS NULL OR outcome_detail IN (''PASSED'',''FAILED_TOTAL'',''FAILED_COMPONENT_FLOOR'',''DEFENSE_ABSENT''));');
END;

IF OBJECT_ID(N'dbo.gate_individual_process_scores', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.gate_individual_process_scores (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_gate_individual_process_scores PRIMARY KEY,
        project_id bigint NOT NULL,
        gate_instance_id bigint NOT NULL,
        student_id bigint NOT NULL,
        supervisor_assignment_id bigint NOT NULL,
        score decimal(5,2) NOT NULL,
        gate_weight_percent decimal(5,2) NOT NULL,
        mentor_reference_notes nvarchar(2000) NULL,
        comments nvarchar(2000) NULL,
        status varchar(20) NOT NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_gate_ind_proc_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL,
        updated_at datetime2(0) NOT NULL,
        finalized_at datetime2(0) NULL,
        CONSTRAINT uq_gate_individual_process_score UNIQUE (gate_instance_id, student_id),
        CONSTRAINT fk_gate_ind_proc_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_gate_ind_proc_gate FOREIGN KEY (gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT fk_gate_ind_proc_student FOREIGN KEY (student_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_gate_ind_proc_supervisor FOREIGN KEY (supervisor_assignment_id) REFERENCES dbo.supervisor_assignments(id),
        CONSTRAINT ck_gate_ind_proc_score CHECK (score BETWEEN 0 AND 10),
        CONSTRAINT ck_gate_ind_proc_weight CHECK (gate_weight_percent > 0 AND gate_weight_percent <= 100),
        CONSTRAINT ck_gate_ind_proc_status CHECK (status IN ('DRAFT','FINALIZED'))
    );
    CREATE INDEX ix_gate_individual_process_project ON dbo.gate_individual_process_scores(project_id, student_id, status);
END;

IF OBJECT_ID(N'dbo.student_defense_absences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.student_defense_absences (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_student_defense_absences PRIMARY KEY,
        project_id bigint NOT NULL,
        student_id bigint NOT NULL,
        recorded_by bigint NOT NULL,
        reason nvarchar(2000) NOT NULL,
        minutes_reference nvarchar(500) NULL,
        recorded_at datetime2(0) NOT NULL,
        CONSTRAINT uq_student_defense_absence UNIQUE (project_id, student_id),
        CONSTRAINT fk_student_defense_absence_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_student_defense_absence_student FOREIGN KEY (student_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_student_defense_absence_actor FOREIGN KEY (recorded_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_student_defense_absence_reason CHECK (LEN(LTRIM(RTRIM(reason))) > 0)
    );
END;

COMMIT TRANSACTION;
GO
