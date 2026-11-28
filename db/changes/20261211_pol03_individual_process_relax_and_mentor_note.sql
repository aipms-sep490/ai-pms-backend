-- TK-W2-09: Relax rubric_id / required_evaluators for POL-03 INDIVIDUAL+ON_GOING and allow independent Mentor notes before Primary draft
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.evaluation_scheme_components', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.evaluation_scheme_components')
          AND name = N'rubric_id' AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.evaluation_scheme_components ALTER COLUMN rubric_id BIGINT NULL;
    END;

    DECLARE @dropEvalCk NVARCHAR(MAX) = N'';
    SELECT @dropEvalCk += N'ALTER TABLE dbo.evaluation_scheme_components DROP CONSTRAINT ' + QUOTENAME(name) + N';'
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.evaluation_scheme_components')
      AND definition LIKE N'%required_evaluators%'
      AND name <> N'ck_eval_scheme_comp_evaluators';
    IF LEN(@dropEvalCk) > 0 EXEC sp_executesql @dropEvalCk;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.evaluation_scheme_components')
          AND name = N'ck_eval_scheme_comp_evaluators'
    )
    BEGIN
        ALTER TABLE dbo.evaluation_scheme_components
            ADD CONSTRAINT ck_eval_scheme_comp_evaluators
            CHECK (
                (scope = N'INDIVIDUAL' AND stage = 'ON_GOING' AND required_evaluators BETWEEN 0 AND 20)
                OR (required_evaluators BETWEEN 1 AND 20 AND rubric_id IS NOT NULL)
            );
    END;
END;
GO

IF OBJECT_ID(N'dbo.gate_individual_process_scores', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.gate_individual_process_scores')
          AND name = N'score' AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.gate_individual_process_scores ALTER COLUMN score DECIMAL(5,2) NULL;
    END;

    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.gate_individual_process_scores')
          AND name = N'supervisor_assignment_id' AND is_nullable = 0
    )
    BEGIN
        ALTER TABLE dbo.gate_individual_process_scores ALTER COLUMN supervisor_assignment_id BIGINT NULL;
    END;

    IF COL_LENGTH(N'dbo.gate_individual_process_scores', N'mentor_note_updated_by') IS NULL
        ALTER TABLE dbo.gate_individual_process_scores ADD mentor_note_updated_by BIGINT NULL;

    IF COL_LENGTH(N'dbo.gate_individual_process_scores', N'mentor_note_updated_at') IS NULL
        ALTER TABLE dbo.gate_individual_process_scores ADD mentor_note_updated_at DATETIME2(0) NULL;
END;
GO

IF OBJECT_ID(N'dbo.gate_individual_process_scores', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_gate_ind_proc_mentor_note_actor'
          AND parent_object_id = OBJECT_ID(N'dbo.gate_individual_process_scores')
    )
    BEGIN
        ALTER TABLE dbo.gate_individual_process_scores
            ADD CONSTRAINT fk_gate_ind_proc_mentor_note_actor
            FOREIGN KEY (mentor_note_updated_by) REFERENCES dbo.users(id);
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_gate_ind_proc_finalized_fields'
          AND parent_object_id = OBJECT_ID(N'dbo.gate_individual_process_scores')
    )
    BEGIN
        ALTER TABLE dbo.gate_individual_process_scores
            ADD CONSTRAINT ck_gate_ind_proc_finalized_fields
            CHECK (status <> 'FINALIZED' OR (score IS NOT NULL AND supervisor_assignment_id IS NOT NULL));
    END;
END;
GO
