SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'AI_PMS_E2E[_]%' THROW 51001, 'Verification must run on an owned E2E database.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'AIPMS_E2E_OWNER' AND CONVERT(nvarchar(100),value)=N'remediation-v1') THROW 51002, 'E2E ownership marker missing.', 1;
IF OBJECT_ID(N'dbo.e2e_aliases', N'U') IS NULL OR (SELECT COUNT(*) FROM dbo.e2e_aliases) < 13 THROW 51003, 'E2E aliases are incomplete.', 1;
IF (SELECT COUNT(*) FROM dbo.users WHERE email LIKE N'%@e2e.invalid') < 9 THROW 51004, 'E2E account seed is incomplete.', 1;
IF (SELECT COUNT(*) FROM dbo.projects WHERE code IN (N'E2E-SINGLE-P',N'E2E-INTER-P')) <> 2 THROW 51005, 'E2E project seed is incomplete.', 1;
IF (SELECT COUNT(*) FROM dbo.team_academic_configurations WHERE project_mode=N'INTERDISCIPLINARY') < 1 THROW 51006, 'Interdisciplinary fixture is missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.teams t WHERE t.code IN (N'E2E-SINGLE',N'E2E-INTER') AND
    (SELECT COUNT(*) FROM dbo.team_members m WHERE m.team_id=t.id AND m.left_at IS NULL) <> 3) THROW 51013, 'Expected three active members per fixture team.', 1;
IF EXISTS (SELECT 1 FROM dbo.team_members tm JOIN dbo.teams t ON t.id=tm.team_id JOIN dbo.users u ON u.id=tm.user_id
    WHERE t.code IN (N'E2E-SINGLE',N'E2E-INTER') AND tm.left_at IS NULL
    AND NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements r WHERE r.team_id=t.id AND r.major_id=u.major_id)) THROW 51014, 'Roster is outside required major scope.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled=1 OR is_not_trusted=1) THROW 51007, 'Disabled or untrusted foreign key found.', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled=1 OR is_not_trusted=1) THROW 51009, 'Disabled or untrusted check found.', 1;
IF EXISTS (SELECT 1 FROM (VALUES ('tasks'),('milestones'),('progress_reports'),('meetings')) t(name)
    WHERE COL_LENGTH('dbo.'+t.name,'concurrency_token') IS NULL) THROW 51010, 'Execution concurrency columns missing.', 1;
IF EXISTS (SELECT 1 FROM (VALUES ('meeting_decisions'),('meeting_action_items'),('evaluation_assignments'),('evaluation_draft_states'),
    ('final_submissions'),('project_results'),('project_result_policies')) t(name)
    WHERE OBJECT_ID('dbo.'+t.name,'U') IS NULL) THROW 51011, 'Workflow table missing.', 1;
IF EXISTS (SELECT 1 FROM (VALUES ('uq_evaluation_assignments_active'),('ix_meeting_decisions_meeting'),('ix_meeting_action_items_meeting'),
    ('ix_result_policy_items_assignment'),('ux_supervisor_assignments_one_primary_active')) t(name)
    WHERE NOT EXISTS (SELECT 1 FROM sys.indexes i WHERE i.name=t.name AND i.is_disabled=0)) THROW 51012, 'Workflow index missing.', 1;
IF EXISTS (SELECT 1 FROM dbo.users WHERE email LIKE N'%@e2e.invalid' AND (password_hash LIKE N'%Aipms%' OR password_hash LIKE N'%password%')) THROW 51008, 'Plaintext-like password detected.', 1;
SELECT DB_NAME() AS database_name,
       (SELECT COUNT(*) FROM dbo.e2e_script_ledger) AS applied_script_count,
       (SELECT COUNT(*) FROM dbo.users WHERE email LIKE N'%@e2e.invalid') AS e2e_account_count,
       (SELECT COUNT(*) FROM dbo.projects WHERE code LIKE N'E2E-%') AS e2e_project_count;
