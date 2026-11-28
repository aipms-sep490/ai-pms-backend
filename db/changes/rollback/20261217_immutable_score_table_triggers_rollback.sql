-- Rollback for 20261217_immutable_score_table_triggers.sql
SET XACT_ABORT ON;
GO
DROP TRIGGER IF EXISTS dbo.tr_immutable_evaluation_finalizations;
DROP TRIGGER IF EXISTS dbo.tr_immutable_project_results;
DROP TRIGGER IF EXISTS dbo.tr_immutable_student_results;
DROP TRIGGER IF EXISTS dbo.tr_immutable_result_revisions;
GO
