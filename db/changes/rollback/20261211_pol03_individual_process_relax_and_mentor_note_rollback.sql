-- Rollback TK-W2-09: Restore evaluation_scheme_components and gate_individual_process_scores constraints
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_gate_ind_proc_finalized_fields' AND parent_object_id = OBJECT_ID(N'dbo.gate_individual_process_scores'))
    ALTER TABLE dbo.gate_individual_process_scores DROP CONSTRAINT ck_gate_ind_proc_finalized_fields;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_gate_ind_proc_mentor_note_actor' AND parent_object_id = OBJECT_ID(N'dbo.gate_individual_process_scores'))
    ALTER TABLE dbo.gate_individual_process_scores DROP CONSTRAINT fk_gate_ind_proc_mentor_note_actor;
IF COL_LENGTH(N'dbo.gate_individual_process_scores', N'mentor_note_updated_at') IS NOT NULL
    ALTER TABLE dbo.gate_individual_process_scores DROP COLUMN mentor_note_updated_at;
IF COL_LENGTH(N'dbo.gate_individual_process_scores', N'mentor_note_updated_by') IS NOT NULL
    ALTER TABLE dbo.gate_individual_process_scores DROP COLUMN mentor_note_updated_by;

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_eval_scheme_comp_evaluators' AND parent_object_id = OBJECT_ID(N'dbo.evaluation_scheme_components'))
    ALTER TABLE dbo.evaluation_scheme_components DROP CONSTRAINT ck_eval_scheme_comp_evaluators;
GO
