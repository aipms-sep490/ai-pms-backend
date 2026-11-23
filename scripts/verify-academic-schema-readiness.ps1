[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$rawConnection = $env:AIPMS_SCHEMA_CHECK_CONNECTION
if ([string]::IsNullOrWhiteSpace($rawConnection)) {
    Write-Error 'Set AIPMS_SCHEMA_CHECK_CONNECTION in the process environment before running this read-only check.'
    exit 3
}

# This check reads metadata only. The release process applies reviewed migrations separately.
$requirements = @(
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.department_specializations'; Column = ''; Migration = '20261204_student_class_and_specializations.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.users'; Column = 'class_code'; Migration = '20261204_student_class_and_specializations.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.users'; Column = 'specialization_id'; Migration = '20261204_student_class_and_specializations.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.users'; Name = 'fk_users_specialization'; Migration = '20261204_student_class_and_specializations.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_pinned_resources'; Column = ''; Migration = '20261203_all_in_one_hub_core.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.evidence_anchored_comments'; Column = ''; Migration = '20261203_all_in_one_hub_core.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.evidence_rubric_criterion_links'; Column = ''; Migration = '20261203_all_in_one_hub_core.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.chat_conversations'; Column = 'channel_scope'; Migration = '20261203_all_in_one_hub_core.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.defense_tech_checks'; Column = ''; Migration = '20261202_optional_defense_room_scheduler_v4.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.defense_standby_pool'; Column = ''; Migration = '20261202_optional_defense_room_scheduler_v4.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.project_periods'; Column = 'cold_window_end_at'; Migration = '20261202_optional_defense_room_scheduler_v4.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.project_periods'; Column = 'defense_buffer_hours'; Migration = '20261202_optional_defense_room_scheduler_v4.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_labels'; Column = ''; Migration = '20261130_decision_supersede_and_task_taxonomy.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.meeting_decisions'; Column = 'superseded_by_decision_id'; Migration = '20261130_decision_supersede_and_task_taxonomy.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.tasks'; Column = 'task_category'; Migration = '20261130_decision_supersede_and_task_taxonomy.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.tasks'; Column = 'deleted_at'; Migration = '20261129_execution_blocker_evidence_softdelete.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.tasks'; Column = 'blocked_since_at'; Migration = '20261129_execution_blocker_evidence_softdelete.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.review_gate_checklist_templates'; Column = ''; Migration = '20261128_document_requirements_final_lock_and_checklists.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_gate_checklists'; Column = ''; Migration = '20261128_document_requirements_final_lock_and_checklists.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_gate_checklist_items'; Column = ''; Migration = '20261128_document_requirements_final_lock_and_checklists.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.final_submission_drafts'; Column = 'lock_workflow_status'; Migration = '20261128_document_requirements_final_lock_and_checklists.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.academic_blueprints'; Column = ''; Migration = '20261128_academic_blueprints_and_planner.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.academic_blueprint_approvals'; Column = ''; Migration = '20261128_academic_blueprints_and_planner.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_blueprint_snapshots'; Column = ''; Migration = '20261128_academic_blueprints_and_planner.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.gate_deadline_change_requests'; Column = ''; Migration = '20261128_academic_blueprints_and_planner.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.project_gate_round_scope_decisions'; Column = ''; Migration = '20261127_tracks_dual_gate_and_sla.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.workflow_sla_notifications'; Column = ''; Migration = '20261127_tracks_dual_gate_and_sla.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.projects'; Column = 'track_code'; Migration = '20261127_tracks_dual_gate_and_sla.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.file_preview_derivatives'; Column = ''; Migration = '20261126_file_preview_derivatives.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.review_gate_packages'; Column = ''; Migration = '20261125_file_scan_and_gate_package_snapshots.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.review_gate_package_items'; Column = ''; Migration = '20261125_file_scan_and_gate_package_snapshots.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.files'; Column = 'scan_status'; Migration = '20261125_file_scan_and_gate_package_snapshots.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.gate_individual_process_scores'; Column = ''; Migration = '20261124_pol01_pol03_scoring_and_optional_defense.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.student_defense_absences'; Column = ''; Migration = '20261124_pol01_pol03_scoring_and_optional_defense.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.project_periods'; Column = 'enable_defense_room_scheduling'; Migration = '20261124_pol01_pol03_scoring_and_optional_defense.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.student_results'; Column = 'outcome_detail'; Migration = '20261124_pol01_pol03_scoring_and_optional_defense.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.evaluation_assignments'; Column = 'valid_from'; Migration = '20261123_assignment_validity_and_scope_indexes.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.review_gate_assignments'; Column = 'valid_from'; Migration = '20261123_assignment_validity_and_scope_indexes.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.assistant_conversations'; Column = 'is_pinned'; Migration = '20261123_upgrade_assistant_conversations_and_feedback.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.academic_knowledge_query_runs'; Column = 'feedback_rating'; Migration = '20261123_upgrade_assistant_conversations_and_feedback.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.ai_copilot_snapshots'; Column = ''; Migration = '20261122_add_ai_copilot_snapshots.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.meeting_transcript_segments'; Column = ''; Migration = '20261122_add_meeting_video_ai.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.meeting_ai_insights'; Column = ''; Migration = '20261122_add_meeting_video_ai.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.task_criteria'; Column = ''; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'id'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'task_id'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'title'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'is_completed'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'sort_order'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'completed_at'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'completed_by'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_criteria'; Column = 'created_by'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.task_criteria'; Name = 'pk_task_criteria'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.task_criteria'; Name = 'ix_task_criteria_task_order'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_criteria'; Name = 'fk_task_criteria_task'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.task_criteria'; Name = 'ck_task_criteria_title'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.task_criteria'; Name = 'ck_task_criteria_completion'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.ai_setup_applications'; Column = ''; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.ai_setup_applications'; Name = 'uq_ai_setup_applications_actor_key'; Migration = '20261122_add_task_criteria.sql' }
    [pscustomobject]@{ Kind = 'CheckAllowsSemesterRollover'; Object = 'dbo.ai_setup_applications'; Name = 'ck_ai_setup_applications_scope'; Migration = '20261124_expand_ai_setup_scopes.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.task_dependency_impacts'; Column = ''; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'id'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'task_id'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'predecessor_task_id'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reopen_history_id'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'status'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reopened_at'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reopened_by'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reopen_reason'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reviewed_at'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'reviewed_by'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.task_dependency_impacts'; Column = 'review_reason'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.task_dependency_impacts'; Name = 'pk_task_dependency_impacts'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.task_dependency_impacts'; Name = 'uq_task_dependency_impacts_reopen'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.task_dependency_impacts'; Name = 'ix_task_dependency_impacts_pending'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_dependency_impacts'; Name = 'fk_task_dependency_impacts_task'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_dependency_impacts'; Name = 'fk_task_dependency_impacts_predecessor'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_dependency_impacts'; Name = 'fk_task_dependency_impacts_history'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_dependency_impacts'; Name = 'fk_task_dependency_impacts_reopened_by'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.task_dependency_impacts'; Name = 'fk_task_dependency_impacts_reviewed_by'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.task_dependency_impacts'; Name = 'ck_task_dependency_impacts_status'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.task_dependency_impacts'; Name = 'ck_task_dependency_impacts_review'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.task_dependency_impacts'; Name = 'ck_task_dependency_impacts_reason'; Migration = '20261118_task_dependency_impacts.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.department_document_templates'; Column = ''; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.rubric_criterion_levels'; Column = ''; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubric_criteria'; Column = 'learning_outcome'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubric_criteria'; Column = 'evidence_guidance'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'major_id'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'project_mode'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'evaluation_stage'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'evaluation_scope'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'effective_from'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'effective_to'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'approval_reference'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.rubric_criterion_levels'; Name = 'pk_rubric_criterion_levels'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.rubric_criterion_levels'; Name = 'uq_rubric_criterion_levels_order'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.rubric_criterion_levels'; Name = 'ix_rubric_criterion_levels_criterion'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.rubric_criterion_levels'; Name = 'fk_rubric_criterion_levels_criterion'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubric_criterion_levels'; Name = 'ck_rubric_criterion_levels_order'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubric_criterion_levels'; Name = 'ck_rubric_criterion_levels_band'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.rubrics'; Name = 'ix_rubrics_scope'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.rubrics'; Name = 'fk_rubrics_major'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubrics'; Name = 'ck_rubrics_project_mode'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubrics'; Name = 'ck_rubrics_evaluation_stage'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubrics'; Name = 'ck_rubrics_evaluation_scope'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubrics'; Name = 'ck_rubrics_scope_major'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.rubrics'; Name = 'ck_rubrics_effective_range'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'NullableColumn'; Object = 'dbo.cold_checklist_items'; Column = 'major_id'; Migration = '20261114_cold_common_checklist.sql' }
    [pscustomobject]@{ Kind = 'CheckAllowsCommon'; Object = 'dbo.cold_checklist_items'; Name = 'ck_cold_item_target'; Migration = '20261114_cold_common_checklist.sql' }
    [pscustomobject]@{ Kind = 'NullableColumn'; Object = 'dbo.cold_report_templates'; Column = 'major_id'; Migration = '20261030_cold_template_common_scope.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.cold_report_templates'; Column = 'scope'; Migration = '20261030_cold_template_common_scope.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.cold_report_templates'; Name = 'ck_cold_template_scope'; Migration = '20261030_cold_template_common_scope.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.cold_report_templates'; Name = 'ck_cold_template_scope_major'; Migration = '20261030_cold_template_common_scope.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.department_document_templates'; Name = 'pk_department_document_templates'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.department_document_templates'; Name = 'ux_department_document_templates_version'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Index'; Object = 'dbo.department_document_templates'; Name = 'ix_department_document_templates_scope'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.department_document_templates'; Name = 'fk_department_document_templates_department'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.department_document_templates'; Name = 'fk_department_document_templates_major'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.department_document_templates'; Name = 'fk_department_document_templates_period'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'ForeignKey'; Object = 'dbo.department_document_templates'; Name = 'fk_department_document_templates_uploader'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.department_document_templates'; Name = 'ck_department_document_templates_version'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.department_document_templates'; Name = 'ck_department_document_templates_status'; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Check'; Object = 'dbo.department_document_templates'; Name = 'ck_department_document_templates_size'; Migration = '20261115_department_document_templates.sql' }
)

try {
    $neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString($rawConnection)
    [void]$neutral.Remove('Command Timeout')
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
    $builder['ApplicationIntent'] = 'ReadOnly'
    $builder['Pooling'] = $false
    $builder['MultipleActiveResultSets'] = $false
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
    try {
        $connection.Open()
        $missing = @()
        foreach ($requirement in $requirements) {
            $command = $connection.CreateCommand()
            try {
                $command.CommandTimeout = 15
                $command.CommandText = switch ($requirement.Kind) {
                    'Table' { "SELECT CASE WHEN OBJECT_ID(@object, N'U') IS NULL THEN 0 ELSE 1 END" }
                    'Column' { 'SELECT CASE WHEN COL_LENGTH(@object, @column) IS NULL THEN 0 ELSE 1 END' }
                    'NullableColumn' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@object, N'U') AND name = @column AND is_nullable = 1) THEN 1 ELSE 0 END" }
                    'Index' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@object, N'U') AND name = @name AND is_disabled = 0) THEN 1 ELSE 0 END" }
                    'ForeignKey' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@object, N'U') AND name = @name AND is_disabled = 0 AND is_not_trusted = 0) THEN 1 ELSE 0 END" }
                    'Check' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@object, N'U') AND name = @name AND is_disabled = 0 AND is_not_trusted = 0) THEN 1 ELSE 0 END" }
                    'CheckAllowsCommon' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@object, N'U') AND name = @name AND is_disabled = 0 AND is_not_trusted = 0 AND definition LIKE '%COMMON%') THEN 1 ELSE 0 END" }
                    'CheckAllowsSemesterRollover' { "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(@object, N'U') AND name = @name AND is_disabled = 0 AND is_not_trusted = 0 AND definition LIKE '%SEMESTER_ROLLOVER%') THEN 1 ELSE 0 END" }
                }
                [void]$command.Parameters.AddWithValue('@object', $requirement.Object)
                if ($requirement.Kind -in @('Column', 'NullableColumn')) { [void]$command.Parameters.AddWithValue('@column', $requirement.Column) }
                if ($requirement.Kind -in @('Index', 'ForeignKey', 'Check', 'CheckAllowsCommon', 'CheckAllowsSemesterRollover')) { [void]$command.Parameters.AddWithValue('@name', $requirement.Name) }
                if ([int]$command.ExecuteScalar() -ne 1) { $missing += $requirement }
            } finally { $command.Dispose() }
        }
    } finally { $connection.Dispose() }
} catch {
    Write-Error 'Academic schema metadata could not be checked. Verify the target connection and read permission locally; connection details are not printed.'
    exit 3
} finally {
    $rawConnection = $null
}

if ($missing.Count -gt 0) {
    Write-Output "NOT_READY: $($missing.Count) required academic schema objects are absent."
    $missing | Sort-Object Migration, Object, Column | ForEach-Object {
        $name = if ($_.Kind -eq 'Table') { $_.Object } elseif ($_.Kind -in @('Column', 'NullableColumn')) { "$($_.Object).$($_.Column)" } else { "$($_.Object).$($_.Name)" }
        Write-Output "MISSING $name -> db/changes/$($_.Migration)"
    }
    exit 2
}

Write-Output "READY: all $($requirements.Count) required academic schema objects are present."
exit 0
