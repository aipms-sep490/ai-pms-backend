[CmdletBinding()]
param([string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Provide the target connection through the process environment.' }
$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
$connection = [System.Data.SqlClient.SqlConnection]::new($neutral.get_ConnectionString())
try {
    $connection.Open()
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 60
    $command.CommandText = @'
SELECT 'table' AS kind,t.name AS capability,CONVERT(bit,CASE WHEN OBJECT_ID('dbo.'+t.name,'U') IS NOT NULL THEN 1 ELSE 0 END) AS ready
FROM (VALUES ('project_registration_snapshots'),('project_department_decisions'),('team_academic_configurations'),
 ('final_submission_drafts'),('final_submissions'),('final_submission_items'),('evaluation_assignments'),
 ('evaluation_draft_states'),('evaluation_finalizations'),('project_result_policies'),('project_results'),
 ('meeting_decisions'),('meeting_action_items'),('project_major_requirements'),('team_major_responsibilities'),
 ('task_disciplines'),('project_evidence'),('contribution_snapshots'),('student_qualifications'),
 ('project_topics'),('notification_email_deliveries'),('scheduled_notification_occurrences'),
 ('cold_report_templates'),('cold_packages'),('cold_checklist_items'),('cold_submissions'),('cold_submission_files'),
 ('weekly_assessments'),('weekly_assessment_scores'),('project_evidence_reviews'),('project_checkpoints'),
 ('project_checkpoint_decisions'),('defense_committees'),('defense_committee_members'),('defense_sessions'),('meeting_notes_versions'),
 ('sprints'),('task_board_labels'),('sprint_task_events')) t(name)
UNION ALL
SELECT 'column',t.name+'.concurrency_token',CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.'+t.name) AND c.name='concurrency_token'
 AND TYPE_NAME(c.system_type_id)='uniqueidentifier' AND c.is_nullable=0 AND c.default_object_id<>0) THEN 1 ELSE 0 END)
FROM (VALUES ('tasks'),('milestones'),('progress_reports'),('meetings'),('meeting_action_items'),('student_qualifications'),('cold_packages'),('cold_submissions'),('weekly_assessments'),('project_checkpoints'),('defense_sessions')) t(name)
UNION ALL
SELECT 'column',t.table_name+'.'+t.name,CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.'+t.table_name) AND c.name=t.name
 AND TYPE_NAME(c.system_type_id)=t.type_name AND c.is_nullable=t.nullable) THEN 1 ELSE 0 END)
FROM (VALUES ('meetings','series_id','uniqueidentifier',1),('meetings','occurrence_number','int',1),
 ('meetings','recurrence','varchar',0),('meetings','notes_version','int',0),('meetings','notes_locked_at','datetime2',1),
 ('meetings','notes_locked_by','bigint',1),('meeting_participants','rsvp_status','varchar',0),('meeting_participants','rsvp_token','uniqueidentifier',0)) t(table_name,name,type_name,nullable)
UNION ALL
SELECT 'column','tasks.'+t.name,CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.tasks') AND c.name=t.name AND TYPE_NAME(c.system_type_id)=t.type_name) THEN 1 ELSE 0 END)
FROM (VALUES ('sprint_id','bigint'),('story_points','int'),('task_code','varchar')) t(name,type_name)
UNION ALL
SELECT 'column','deliverables.'+t.name,CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.deliverables') AND c.name=t.name
 AND TYPE_NAME(c.system_type_id)=t.type_name AND c.is_nullable=1) THEN 1 ELSE 0 END)
FROM (VALUES ('evidence_scope','varchar'),('target_major_id','bigint'),('target_student_id','bigint')) t(name,type_name)
UNION ALL
SELECT 'column',t.table_name+'.'+t.name,CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.'+t.table_name) AND c.name=t.name
 AND TYPE_NAME(c.system_type_id)=t.type_name AND c.is_nullable=1) THEN 1 ELSE 0 END)
FROM (VALUES ('evaluation_schemes','governance_json','nvarchar'),('evaluation_schemes','locked_at','datetime2'),
 ('evaluation_scheme_components','evaluator_weights_json','nvarchar'),('evaluation_scheme_components','stage','varchar'),
 ('evaluation_assignments','stage','varchar'),('evaluation_assignments','cold_package_id','bigint')) t(table_name,name,type_name)
UNION ALL
SELECT 'constraint',t.name,CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.check_constraints WHERE name=t.name AND is_disabled=0 AND is_not_trusted=0) THEN 1 ELSE 0 END)
FROM (VALUES ('ck_scheme_governance_json'),('ck_component_evaluator_weights_json'),('ck_cold_template_json'),
 ('ck_cold_package_json'),('ck_cold_package_status'),('ck_cold_item_target'),('ck_cold_submission_status'),('ck_cold_file_size'),
 ('ck_assignment_stage'),('ck_scheme_component_stage'),('ck_weekly_status'),('ck_weekly_dates'),('ck_weekly_rubric_json'),('ck_weekly_total'),('ck_weekly_score'),
 ('ck_evidence_review_json'),('ck_evidence_review_decision'),('ck_checkpoint_decision'),('ck_checkpoint_evidence'),('ck_defense_dates'),('ck_defense_status'),
 ('ck_meeting_recurrence'),('ck_meeting_notes_lock'),('ck_meeting_rsvp'),('ck_notes_version'),
 ('ck_sprint_status'),('ck_sprint_dates'),('ck_sprint_lifecycle'),('ck_task_story_points'),('ck_sprint_event_kind'),('ck_sprint_event_points')) t(name)
UNION ALL
SELECT 'constraint','ck_deliverables_evidence_target',CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID('dbo.deliverables')
 AND name='ck_deliverables_evidence_target' AND is_disabled=0 AND is_not_trusted=0) THEN 1 ELSE 0 END)
UNION ALL
SELECT 'index',t.table_name+'.'+t.index_name,CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i
 WHERE i.object_id=OBJECT_ID('dbo.'+t.table_name) AND i.name=t.index_name AND i.is_disabled=0) THEN 1 ELSE 0 END)
FROM (VALUES ('evaluation_assignments','uq_scoped_evaluation_assignment'),('evaluation_assignments','uq_legacy_evaluation_assignment'),('meeting_decisions','ix_meeting_decisions_meeting'),
 ('meeting_action_items','ix_meeting_action_items_meeting'),('project_result_policy_items','ix_result_policy_items_assignment'),
 ('supervisor_assignments','ux_supervisor_assignments_one_primary_active'),
 ('contribution_snapshots','ux_contribution_snapshots_hash'),('deliverables','ix_deliverables_evidence_target'),
 ('cold_submissions','ux_cold_submission_draft'),('cold_submissions','uq_cold_submission_version'),('cold_packages','uq_cold_package_project'),
 ('evaluation_assignments','ix_assignment_cold_package'),('weekly_assessments','ux_weekly_draft'),('weekly_assessments','uq_weekly_version'),
 ('project_evidence_reviews','ix_evidence_review_history'),('project_checkpoints','ix_checkpoint_project'),('project_checkpoint_decisions','ix_checkpoint_decisions'),
 ('defense_committees','ix_defense_committee_project'),('defense_sessions','ux_defense_scheduled_project'),('defense_sessions','ix_defense_schedule'),
 ('meetings','ux_meeting_occurrence'),('meeting_notes_versions','uq_notes_version'),('sprints','ux_sprint_active'),
 ('sprints','ix_sprint_project'),('tasks','ux_task_code'),('tasks','ix_task_sprint'),('task_board_labels','ix_board_label'),('sprint_task_events','ix_sprint_events')) t(table_name,index_name)
UNION ALL
SELECT 'foreign_key',t.name,CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys f
 WHERE f.name=t.name AND f.is_disabled=0 AND f.is_not_trusted=0) THEN 1 ELSE 0 END)
FROM (VALUES ('fk_evaluation_assignments_project'),('fk_evaluation_assignments_evaluator'),('fk_evaluation_assignments_rubric'),
 ('fk_evaluation_assignments_period'),('fk_evaluation_draft_states_assignment'),('fk_final_submissions_project'),
 ('fk_final_submission_items_submission'),('fk_project_results_submission'),('fk_result_policy_items_assignment'),
 ('fk_meeting_decisions_meeting'),('fk_meeting_decisions_user'),('fk_meeting_action_items_meeting'),
 ('fk_meeting_action_items_assignee'),('fk_meeting_action_items_creator'),
 ('fk_contribution_snapshots_project'),('fk_contribution_snapshots_user'),('fk_deliverables_target_major'),('fk_deliverables_target_student'),
 ('fk_cold_package_project'),('fk_cold_item_package'),('fk_cold_item_template'),('fk_cold_submission_item'),('fk_cold_file_submission'),('fk_cold_file_file'),
 ('fk_assignment_cold_package'),('fk_weekly_project'),('fk_weekly_student'),('fk_weekly_primary'),('fk_weekly_rubric'),('fk_weekly_score_assessment'),
 ('fk_evidence_review_evidence'),('fk_evidence_review_version'),('fk_evidence_review_assignment'),('fk_checkpoint_project'),('fk_checkpoint_decision_checkpoint'),
 ('fk_defense_committee_project'),('fk_defense_member_committee'),('fk_defense_member_user'),('fk_defense_session_project'),('fk_defense_session_committee'),
 ('fk_meeting_notes_locker'),('fk_notes_version_meeting'),('fk_notes_version_actor'),('fk_sprint_project'),('fk_sprint_creator'),('fk_task_sprint'),
 ('fk_board_label_task'),('fk_sprint_event_sprint'),('fk_sprint_event_task'),('fk_sprint_event_actor')) t(name)
UNION ALL
SELECT 'constraint','all_foreign_keys_enabled_and_trusted',CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1) THEN 0 ELSE 1 END)
UNION ALL
SELECT 'constraint','all_checks_enabled_and_trusted',CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1) THEN 0 ELSE 1 END);
'@
    $reader = $command.ExecuteReader()
    $checks = @()
    try {
        while ($reader.Read()) { $checks += [pscustomobject]@{ kind=$reader.GetString(0); capability=$reader.GetString(1); ready=$reader.GetBoolean(2) } }
    } finally { $reader.Dispose(); $command.Dispose() }
    [pscustomobject]@{
        asOfUtc = [DateTime]::UtcNow.ToString('o')
        scope = 'Remediation schema capabilities; not full application or provider readiness'
        ready = @($checks | Where-Object { -not $_.ready }).Count -eq 0
        checks = $checks
    } | ConvertTo-Json -Depth 4
    if (@($checks | Where-Object { -not $_.ready }).Count -gt 0) { throw 'Schema readiness failed; inspect the capability report.' }
} finally { $connection.Dispose() }
