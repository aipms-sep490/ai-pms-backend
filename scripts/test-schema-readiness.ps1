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
 ('meeting_decisions'),('meeting_action_items')) t(name)
UNION ALL
SELECT 'column',t.name+'.concurrency_token',CONVERT(bit,CASE WHEN EXISTS (
 SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID('dbo.'+t.name) AND c.name='concurrency_token'
 AND TYPE_NAME(c.system_type_id)='uniqueidentifier' AND c.is_nullable=0 AND c.default_object_id<>0) THEN 1 ELSE 0 END)
FROM (VALUES ('tasks'),('milestones'),('progress_reports'),('meetings'),('meeting_action_items')) t(name)
UNION ALL
SELECT 'index',t.table_name+'.'+t.index_name,CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i
 WHERE i.object_id=OBJECT_ID('dbo.'+t.table_name) AND i.name=t.index_name AND i.is_disabled=0) THEN 1 ELSE 0 END)
FROM (VALUES ('evaluation_assignments','uq_evaluation_assignments_active'),('meeting_decisions','ix_meeting_decisions_meeting'),
 ('meeting_action_items','ix_meeting_action_items_meeting'),('project_result_policy_items','ix_result_policy_items_assignment'),
 ('supervisor_assignments','ux_supervisor_assignments_one_primary_active')) t(table_name,index_name)
UNION ALL
SELECT 'foreign_key',t.name,CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys f
 WHERE f.name=t.name AND f.is_disabled=0 AND f.is_not_trusted=0) THEN 1 ELSE 0 END)
FROM (VALUES ('fk_evaluation_assignments_project'),('fk_evaluation_assignments_evaluator'),('fk_evaluation_assignments_rubric'),
 ('fk_evaluation_assignments_period'),('fk_evaluation_draft_states_assignment'),('fk_final_submissions_project'),
 ('fk_final_submission_items_submission'),('fk_project_results_submission'),('fk_result_policy_items_assignment'),
 ('fk_meeting_decisions_meeting'),('fk_meeting_decisions_user'),('fk_meeting_action_items_meeting'),
 ('fk_meeting_action_items_assignee'),('fk_meeting_action_items_creator')) t(name)
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
