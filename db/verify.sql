/*
 AI-PMS verification script.
 Run after schema.sql and seed.sql.

 Checks:
 - 44 required tables
 - Required system roles
 - SUPERVISOR is not seeded as a separate role
 - Foreign-key relationships
 - CHECK constraints
 - UNIQUE constraints / filtered unique indexes
 - Common query indexes
 - Important defaults and column rules
 - One active team per student per semester
 - Controlled project-period types
 - Rubric-based evaluation structure

 This script does not modify business data.
*/

USE [AI_PMS];
GO
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;

/* =========================================================
   1. REQUIRED TABLES
   ========================================================= */

DECLARE @expected_tables TABLE (
    table_name SYSNAME PRIMARY KEY
);

INSERT INTO @expected_tables(table_name) VALUES
    (N'organizations'),
    (N'departments'),
    (N'majors'),
    (N'academic_semesters'),
    (N'project_periods'),
    (N'roles'),
    (N'users'),
    (N'user_roles'),
    (N'permissions'),
    (N'role_permissions'),
    (N'refresh_tokens'),
    (N'password_reset_tokens'),
    (N'audit_logs'),
    (N'teams'),
    (N'team_members'),
    (N'team_invitations'),
    (N'projects'),
    (N'project_majors'),
    (N'project_status_history'),
    (N'supervisor_profiles'),
    (N'supervisor_expertise'),
    (N'supervisor_requests'),
    (N'supervisor_assignments'),
    (N'milestones'),
    (N'tasks'),
    (N'task_assignees'),
    (N'task_dependencies'),
    (N'task_status_history'),
    (N'progress_reports'),
    (N'meetings'),
    (N'meeting_participants'),
    (N'deliverables'),
    (N'deliverable_versions'),
    (N'supervisor_feedback'),
    (N'files'),
    (N'evaluation_criteria'),
    (N'rubrics'),
    (N'rubric_criteria'),
    (N'evaluations'),
    (N'evaluation_details'),
    (N'notifications'),
    (N'notification_recipients'),
    (N'tags'),
    (N'project_tags');

IF (SELECT COUNT(*) FROM @expected_tables) <> 44
BEGIN
    THROW 51001, 'Verification script error: expected table list is not 44.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM @expected_tables e
    WHERE OBJECT_ID(N'dbo.' + e.table_name, N'U') IS NULL
)
BEGIN
    SELECT e.table_name AS missing_table
    FROM @expected_tables e
    WHERE OBJECT_ID(N'dbo.' + e.table_name, N'U') IS NULL;

    THROW 51000, 'Verification failed: one or more required tables are missing.', 1;
END;

/* =========================================================
   2. REQUIRED SYSTEM ROLES
   ========================================================= */

DECLARE @required_roles TABLE (
    code NVARCHAR(50) PRIMARY KEY
);

INSERT INTO @required_roles(code) VALUES
    (N'ADMIN'),
    (N'DEPARTMENT_STAFF'),
    (N'LECTURER'),
    (N'STUDENT');

IF EXISTS (
    SELECT 1
    FROM @required_roles rr
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.roles r WHERE r.code = rr.code
    )
)
BEGIN
    SELECT rr.code AS missing_role
    FROM @required_roles rr
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.roles r WHERE r.code = rr.code
    );

    THROW 51002, 'Verification failed: required system roles are missing.', 1;
END;

IF EXISTS (SELECT 1 FROM dbo.roles WHERE code = N'SUPERVISOR')
BEGIN
    THROW 51003, 'Verification failed: SUPERVISOR must not be seeded as a system role.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM @required_roles rr
    JOIN dbo.roles r ON r.code = rr.code
    WHERE r.is_system_role <> 1
)
BEGIN
    THROW 51004, 'Verification failed: one or more required roles are not system roles.', 1;
END;

/* =========================================================
   3. REQUIRED FOREIGN KEYS
   ========================================================= */

DECLARE @required_fks TABLE (
    table_name SYSNAME NOT NULL,
    fk_name SYSNAME NOT NULL,
    PRIMARY KEY (table_name, fk_name)
);

INSERT INTO @required_fks(table_name, fk_name) VALUES
    (N'departments', N'fk_departments_organization'),
    (N'majors', N'fk_majors_department'),
    (N'academic_semesters', N'fk_academic_semesters_organization'),
    (N'project_periods', N'fk_project_periods_semester'),
    (N'users', N'fk_users_department'),
    (N'users', N'fk_users_major'),
    (N'user_roles', N'fk_user_roles_user'),
    (N'user_roles', N'fk_user_roles_role'),
    (N'user_roles', N'fk_user_roles_assigned_by'),
    (N'role_permissions', N'fk_role_permissions_role'),
    (N'role_permissions', N'fk_role_permissions_permission'),
    (N'role_permissions', N'fk_role_permissions_assigned_by'),
    (N'refresh_tokens', N'fk_refresh_tokens_user'),
    (N'refresh_tokens', N'fk_refresh_tokens_replaced_by'),
    (N'password_reset_tokens', N'fk_password_reset_tokens_user'),
    (N'audit_logs', N'fk_audit_logs_actor'),
    (N'project_tags', N'fk_project_tags_project'),
    (N'project_tags', N'fk_project_tags_tag'),
    (N'teams', N'fk_teams_semester'),
    (N'teams', N'fk_teams_created_by'),
    (N'team_members', N'fk_team_members_team'),
    (N'team_members', N'fk_team_members_user'),
    (N'team_invitations', N'fk_team_invitations_team'),
    (N'team_invitations', N'fk_team_invitations_invited_user'),
    (N'team_invitations', N'fk_team_invitations_invited_by'),
    (N'projects', N'fk_projects_team'),
    (N'projects', N'fk_projects_created_by'),
    (N'project_majors', N'fk_project_majors_project'),
    (N'project_majors', N'fk_project_majors_major'),
    (N'project_status_history', N'fk_project_status_history_project'),
    (N'project_status_history', N'fk_project_status_history_changed_by'),
    (N'supervisor_profiles', N'fk_supervisor_profiles_user'),
    (N'supervisor_expertise', N'fk_supervisor_expertise_profile'),
    (N'supervisor_requests', N'fk_supervisor_requests_project'),
    (N'supervisor_requests', N'fk_supervisor_requests_profile'),
    (N'supervisor_requests', N'fk_supervisor_requests_requested_by'),
    (N'supervisor_assignments', N'fk_supervisor_assignments_project'),
    (N'supervisor_assignments', N'fk_supervisor_assignments_profile'),
    (N'supervisor_assignments', N'fk_supervisor_assignments_request'),
    (N'milestones', N'fk_milestones_project'),
    (N'milestones', N'fk_milestones_created_by'),
    (N'tasks', N'fk_tasks_milestone'),
    (N'tasks', N'fk_tasks_parent'),
    (N'tasks', N'fk_tasks_created_by'),
    (N'task_assignees', N'fk_task_assignees_task'),
    (N'task_assignees', N'fk_task_assignees_user'),
    (N'task_assignees', N'fk_task_assignees_assigned_by'),
    (N'task_dependencies', N'fk_task_dependencies_task'),
    (N'task_dependencies', N'fk_task_dependencies_depends_on'),
    (N'task_status_history', N'fk_task_status_history_task'),
    (N'task_status_history', N'fk_task_status_history_changed_by'),
    (N'progress_reports', N'fk_progress_reports_project'),
    (N'progress_reports', N'fk_progress_reports_submitted_by'),
    (N'meetings', N'fk_meetings_project'),
    (N'meetings', N'fk_meetings_created_by'),
    (N'meeting_participants', N'fk_meeting_participants_meeting'),
    (N'meeting_participants', N'fk_meeting_participants_user'),
    (N'deliverables', N'fk_deliverables_project'),
    (N'deliverables', N'fk_deliverables_milestone'),
    (N'deliverables', N'fk_deliverables_created_by'),
    (N'deliverable_versions', N'fk_deliverable_versions_deliverable'),
    (N'deliverable_versions', N'fk_deliverable_versions_submitted_by'),
    (N'supervisor_feedback', N'fk_supervisor_feedback_project'),
    (N'supervisor_feedback', N'fk_supervisor_feedback_assignment'),
    (N'supervisor_feedback', N'fk_supervisor_feedback_progress_report'),
    (N'supervisor_feedback', N'fk_supervisor_feedback_deliverable_version'),
    (N'supervisor_feedback', N'fk_supervisor_feedback_meeting'),
    (N'files', N'fk_files_uploaded_by'),
    (N'files', N'fk_files_deliverable_version'),
    (N'files', N'fk_files_progress_report'),
    (N'files', N'fk_files_meeting'),
    (N'files', N'fk_files_supervisor_feedback'),
    (N'rubrics', N'fk_rubrics_department'),
    (N'rubrics', N'fk_rubrics_semester'),
    (N'rubrics', N'fk_rubrics_created_by'),
    (N'rubrics', N'fk_rubrics_approved_by'),
    (N'rubric_criteria', N'fk_rubric_criteria_rubric'),
    (N'rubric_criteria', N'fk_rubric_criteria_criterion'),
    (N'evaluations', N'fk_evaluations_project'),
    (N'evaluations', N'fk_evaluations_evaluator'),
    (N'evaluations', N'fk_evaluations_rubric'),
    (N'evaluations', N'fk_evaluations_finalized_by'),
    (N'evaluation_details', N'fk_evaluation_details_evaluation'),
    (N'evaluation_details', N'fk_evaluation_details_rubric_criterion'),
    (N'notifications', N'fk_notifications_created_by'),
    (N'notification_recipients', N'fk_notification_recipients_notification'),
    (N'notification_recipients', N'fk_notification_recipients_user');

IF EXISTS (
    SELECT 1
    FROM @required_fks rf
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.' + rf.table_name)
          AND fk.name = rf.fk_name
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
    )
)
BEGIN
    SELECT rf.table_name, rf.fk_name AS missing_or_disabled_foreign_key
    FROM @required_fks rf
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.' + rf.table_name)
          AND fk.name = rf.fk_name
          AND fk.is_disabled = 0
          AND fk.is_not_trusted = 0
    );

    THROW 51005, 'Verification failed: required foreign key missing or disabled.', 1;
END;

/* team_members(team_id, academic_semester_id) must map to teams(id, academic_semester_id). */
IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns pc
      ON pc.object_id = fkc.parent_object_id
     AND pc.column_id = fkc.parent_column_id
    JOIN sys.columns rc
      ON rc.object_id = fkc.referenced_object_id
     AND rc.column_id = fkc.referenced_column_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.team_members')
      AND fk.name = N'fk_team_members_team'
      AND pc.name = N'team_id'
      AND rc.name = N'id'
)
OR NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys fk
    JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
    JOIN sys.columns pc
      ON pc.object_id = fkc.parent_object_id
     AND pc.column_id = fkc.parent_column_id
    JOIN sys.columns rc
      ON rc.object_id = fkc.referenced_object_id
     AND rc.column_id = fkc.referenced_column_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.team_members')
      AND fk.name = N'fk_team_members_team'
      AND pc.name = N'academic_semester_id'
      AND rc.name = N'academic_semester_id'
)
BEGIN
    THROW 51006, 'Verification failed: team_members composite FK mapping is invalid.', 1;
END;

/* =========================================================
   4. REQUIRED CHECK CONSTRAINTS
   ========================================================= */

DECLARE @required_checks TABLE (
    table_name SYSNAME NOT NULL,
    check_name SYSNAME NOT NULL,
    PRIMARY KEY (table_name, check_name)
);

INSERT INTO @required_checks(table_name, check_name) VALUES
    (N'academic_semesters', N'ck_academic_semesters_dates'),
    (N'academic_semesters', N'ck_academic_semesters_status'),
    (N'project_periods', N'ck_project_periods_dates'),
    (N'project_periods', N'ck_project_periods_type'),
    (N'project_periods', N'ck_project_periods_status'),
    (N'users', N'ck_users_status'),
    (N'users', N'ck_users_access_failed_count'),
    (N'refresh_tokens', N'ck_refresh_tokens_expires_at'),
    (N'refresh_tokens', N'ck_refresh_tokens_revoked_at'),
    (N'refresh_tokens', N'ck_refresh_tokens_reuse_detected_at'),
    (N'password_reset_tokens', N'ck_password_reset_tokens_expires_at'),
    (N'password_reset_tokens', N'ck_password_reset_tokens_used_at'),
    (N'audit_logs', N'ck_audit_logs_outcome'),
    (N'audit_logs', N'ck_audit_logs_details_json'),
    (N'tags', N'ck_tags_type'),
    (N'teams', N'ck_teams_status'),
    (N'team_members', N'ck_team_members_left_at'),
    (N'team_invitations', N'ck_team_invitations_status'),
    (N'projects', N'ck_projects_status'),
    (N'project_status_history', N'ck_project_status_history_old'),
    (N'project_status_history', N'ck_project_status_history_new'),
    (N'supervisor_profiles', N'ck_supervisor_profiles_capacity'),
    (N'supervisor_requests', N'ck_supervisor_requests_status'),
    (N'supervisor_assignments', N'ck_supervisor_assignments_dates'),
    (N'milestones', N'ck_milestones_dates'),
    (N'milestones', N'ck_milestones_status'),
    (N'tasks', N'ck_tasks_status'),
    (N'tasks', N'ck_tasks_priority'),
    (N'tasks', N'ck_tasks_dates'),
    (N'task_dependencies', N'ck_task_dependencies_not_self'),
    (N'task_dependencies', N'ck_task_dependencies_type'),
    (N'task_status_history', N'ck_task_status_history_old'),
    (N'task_status_history', N'ck_task_status_history_new'),
    (N'progress_reports', N'ck_progress_reports_type'),
    (N'progress_reports', N'ck_progress_reports_status'),
    (N'progress_reports', N'ck_progress_reports_dates'),
    (N'meetings', N'ck_meetings_dates'),
    (N'meetings', N'ck_meetings_status'),
    (N'meeting_participants', N'ck_meeting_participants_attendance'),
    (N'deliverables', N'ck_deliverables_status'),
    (N'deliverable_versions', N'ck_deliverable_versions_number'),
    (N'deliverable_versions', N'ck_deliverable_versions_status'),
    (N'files', N'ck_files_size'),
    (N'files', N'ck_files_single_parent'),
    (N'rubrics', N'ck_rubrics_scope'),
    (N'rubrics', N'ck_rubrics_version_number'),
    (N'rubrics', N'ck_rubrics_approval_status'),
    (N'rubrics', N'ck_rubrics_approval_audit'),
    (N'rubric_criteria', N'ck_rubric_criteria_weight'),
    (N'rubric_criteria', N'ck_rubric_criteria_max_score'),
    (N'evaluations', N'ck_evaluations_type'),
    (N'evaluations', N'ck_evaluations_status'),
    (N'evaluations', N'ck_evaluations_total_score'),
    (N'evaluations', N'ck_evaluations_rounding_rule'),
    (N'evaluations', N'ck_evaluations_finalize_audit'),
    (N'evaluation_details', N'ck_evaluation_details_score'),
    (N'notification_recipients', N'ck_notification_recipients_read_at');

IF EXISTS (
    SELECT 1
    FROM @required_checks rc
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id = OBJECT_ID(N'dbo.' + rc.table_name)
          AND cc.name = rc.check_name
          AND cc.is_disabled = 0
    )
)
BEGIN
    SELECT rc.table_name, rc.check_name AS missing_or_disabled_check
    FROM @required_checks rc
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints cc
        WHERE cc.parent_object_id = OBJECT_ID(N'dbo.' + rc.table_name)
          AND cc.name = rc.check_name
          AND cc.is_disabled = 0
    );

    THROW 51007, 'Verification failed: required CHECK constraint missing or disabled.', 1;
END;

DECLARE @period_type_check NVARCHAR(MAX);

SELECT @period_type_check = cc.definition
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.project_periods')
  AND cc.name = N'ck_project_periods_type';

IF @period_type_check IS NULL
   OR @period_type_check NOT LIKE N'%REGISTRATION%'
   OR @period_type_check NOT LIKE N'%PROJECT_REVIEW%'
   OR @period_type_check NOT LIKE N'%SUPERVISOR_SELECTION%'
   OR @period_type_check NOT LIKE N'%EXECUTION%'
   OR @period_type_check NOT LIKE N'%FINAL_SUBMISSION%'
   OR @period_type_check NOT LIKE N'%EVALUATION%'
BEGIN
    THROW 51008, 'Verification failed: project_periods.period_type CHECK is incomplete.', 1;
END;

/* =========================================================
   5. UNIQUE CONSTRAINTS / FILTERED UNIQUE INDEXES
   ========================================================= */

DECLARE @required_unique_indexes TABLE (
    table_name SYSNAME NOT NULL,
    index_name SYSNAME NOT NULL,
    PRIMARY KEY (table_name, index_name)
);

INSERT INTO @required_unique_indexes(table_name, index_name) VALUES
    (N'organizations', N'uq_organizations_code'),
    (N'departments', N'uq_departments_org_code'),
    (N'majors', N'uq_majors_department_code'),
    (N'academic_semesters', N'uq_academic_semesters_org_code'),
    (N'project_periods', N'uq_project_periods_semester_code'),
    (N'roles', N'uq_roles_code'),
    (N'users', N'uq_users_email'),
    (N'user_roles', N'uq_user_roles_user_role'),
    (N'role_permissions', N'pk_role_permissions'),
    (N'permissions', N'uq_permissions_code'),
    (N'refresh_tokens', N'uq_refresh_tokens_token_hash'),
    (N'password_reset_tokens', N'uq_password_reset_tokens_token_hash'),
    (N'teams', N'uq_teams_semester_code'),
    (N'teams', N'uq_teams_id_semester'),
    (N'team_members', N'uq_team_members_team_user'),
    (N'projects', N'uq_projects_code'),
    (N'projects', N'uq_projects_active_team'),
    (N'project_majors', N'uq_project_majors_project_major'),
    (N'tags', N'uq_tags_normalized_name_type'),
    (N'project_tags', N'pk_project_tags'),
    (N'supervisor_profiles', N'uq_supervisor_profiles_user'),
    (N'supervisor_expertise', N'uq_supervisor_expertise_name'),
    (N'supervisor_assignments', N'uq_supervisor_assignments_request'),
    (N'supervisor_assignments', N'uq_supervisor_assignments_project_supervisor'),
    (N'task_assignees', N'uq_task_assignees_task_user'),
    (N'task_dependencies', N'uq_task_dependencies_pair'),
    (N'progress_reports', N'uq_progress_reports_period'),
    (N'meeting_participants', N'uq_meeting_participants_meeting_user'),
    (N'deliverable_versions', N'uq_deliverable_versions_number'),
    (N'evaluation_criteria', N'uq_evaluation_criteria_code'),
    (N'rubrics', N'uq_rubrics_code_version'),
    (N'rubric_criteria', N'uq_rubric_criteria_rubric_criterion'),
    (N'evaluation_details', N'uq_evaluation_details_eval_rubric_criterion'),
    (N'notification_recipients', N'uq_notification_recipients_notification_user'),
    (N'users', N'ux_users_student_code_not_null'),
    (N'users', N'ux_users_employee_code_not_null'),
    (N'team_members', N'ux_team_members_one_leader_per_team'),
    (N'team_members', N'ux_team_members_one_active_team_per_semester'),
    (N'team_invitations', N'ux_team_invitations_pending'),
    (N'supervisor_requests', N'ux_supervisor_requests_pending'),
    (N'supervisor_assignments', N'ux_supervisor_assignments_one_primary_active');

IF EXISTS (
    SELECT 1
    FROM @required_unique_indexes rui
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.' + rui.table_name)
          AND i.name = rui.index_name
          AND i.is_unique = 1
          AND i.is_disabled = 0
    )
)
BEGIN
    SELECT rui.table_name, rui.index_name AS missing_or_disabled_unique_index
    FROM @required_unique_indexes rui
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.' + rui.table_name)
          AND i.name = rui.index_name
          AND i.is_unique = 1
          AND i.is_disabled = 0
    );

    THROW 51009, 'Verification failed: required UNIQUE constraint/index missing or disabled.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.team_members')
      AND i.name = N'ux_team_members_one_leader_per_team'
      AND i.is_unique = 1
      AND i.has_filter = 1
      AND i.is_disabled = 0
)
BEGIN
    THROW 51010, 'Verification failed: active team leader index is invalid or missing.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.team_members')
      AND i.name = N'ux_team_members_one_active_team_per_semester'
      AND i.is_unique = 1
      AND i.has_filter = 1
      AND i.is_disabled = 0
)
BEGIN
    THROW 51011, 'Verification failed: one-active-team-per-semester index is invalid or missing.', 1;
END;

/* =========================================================
   6. REQUIRED QUERY INDEXES
   ========================================================= */

DECLARE @required_indexes TABLE (
    table_name SYSNAME NOT NULL,
    index_name SYSNAME NOT NULL,
    PRIMARY KEY (table_name, index_name)
);

INSERT INTO @required_indexes(table_name, index_name) VALUES
    (N'departments', N'ix_departments_organization_id'),
    (N'majors', N'ix_majors_department_id'),
    (N'academic_semesters', N'ix_academic_semesters_organization_status'),
    (N'project_periods', N'ix_project_periods_semester_status'),
    (N'users', N'ix_users_department_id'),
    (N'users', N'ix_users_major_id'),
    (N'user_roles', N'ix_user_roles_role_id'),
    (N'user_roles', N'ix_user_roles_assigned_by'),
    (N'role_permissions', N'ix_role_permissions_permission_id'),
    (N'role_permissions', N'ix_role_permissions_assigned_by'),
    (N'refresh_tokens', N'ix_refresh_tokens_user_active'),
    (N'refresh_tokens', N'ix_refresh_tokens_family_id'),
    (N'password_reset_tokens', N'ix_password_reset_tokens_user_active'),
    (N'audit_logs', N'ix_audit_logs_occurred_at'),
    (N'audit_logs', N'ix_audit_logs_actor_occurred_at'),
    (N'audit_logs', N'ix_audit_logs_entity'),
    (N'audit_logs', N'ix_audit_logs_correlation_id'),
    (N'teams', N'ix_teams_semester_status'),
    (N'team_members', N'ix_team_members_user_id'),
    (N'team_members', N'ix_team_members_semester_user'),
    (N'team_invitations', N'ix_team_invitations_invited_user_status'),
    (N'projects', N'ix_projects_status'),
    (N'project_majors', N'ix_project_majors_major_id'),
    (N'project_tags', N'ix_project_tags_tag_project'),
    (N'project_status_history', N'ix_project_status_history_project_changed_at'),
    (N'supervisor_expertise', N'ix_supervisor_expertise_profile_id'),
    (N'supervisor_requests', N'ix_supervisor_requests_project_status'),
    (N'supervisor_requests', N'ix_supervisor_requests_supervisor_status'),
    (N'supervisor_assignments', N'ix_supervisor_assignments_project'),
    (N'supervisor_assignments', N'ix_supervisor_assignments_supervisor'),
    (N'milestones', N'ix_milestones_project_status'),
    (N'tasks', N'ix_tasks_milestone_status'),
    (N'tasks', N'ix_tasks_parent_task_id'),
    (N'task_assignees', N'ix_task_assignees_user_id'),
    (N'task_dependencies', N'ix_task_dependencies_depends_on'),
    (N'task_status_history', N'ix_task_status_history_task_changed_at'),
    (N'progress_reports', N'ix_progress_reports_project_period'),
    (N'meetings', N'ix_meetings_project_start_at'),
    (N'meeting_participants', N'ix_meeting_participants_user_id'),
    (N'deliverables', N'ix_deliverables_project_status'),
    (N'deliverables', N'ix_deliverables_milestone_id'),
    (N'deliverable_versions', N'ix_deliverable_versions_deliverable_id'),
    (N'supervisor_feedback', N'ix_supervisor_feedback_project_id'),
    (N'files', N'ix_files_uploaded_by'),
    (N'files', N'ix_files_deliverable_version_id'),
    (N'files', N'ix_files_progress_report_id'),
    (N'files', N'ix_files_meeting_id'),
    (N'rubrics', N'ix_rubrics_department_active'),
    (N'rubrics', N'ix_rubrics_semester_active'),
    (N'rubric_criteria', N'ix_rubric_criteria_rubric_sort'),
    (N'rubric_criteria', N'ix_rubric_criteria_criterion_id'),
    (N'evaluations', N'ix_evaluations_project_type_status'),
    (N'evaluations', N'ix_evaluations_evaluator_id'),
    (N'evaluations', N'ix_evaluations_rubric_id'),
    (N'evaluation_details', N'ix_evaluation_details_rubric_criterion_id'),
    (N'notifications', N'ix_notifications_related_entity'),
    (N'notification_recipients', N'ix_notification_recipients_user_read');

IF EXISTS (
    SELECT 1
    FROM @required_indexes ri
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.' + ri.table_name)
          AND i.name = ri.index_name
          AND i.is_disabled = 0
    )
)
BEGIN
    SELECT ri.table_name, ri.index_name AS missing_or_disabled_index
    FROM @required_indexes ri
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.indexes i
        WHERE i.object_id = OBJECT_ID(N'dbo.' + ri.table_name)
          AND i.name = ri.index_name
          AND i.is_disabled = 0
    );

    THROW 51012, 'Verification failed: required query index missing or disabled.', 1;
END;

/* =========================================================
   7. IMPORTANT COLUMN EXPECTATIONS
   ========================================================= */

DECLARE @required_columns TABLE (
    table_name SYSNAME NOT NULL,
    column_name SYSNAME NOT NULL,
    expected_nullable BIT NOT NULL,
    PRIMARY KEY (table_name, column_name)
);

INSERT INTO @required_columns(table_name, column_name, expected_nullable)
VALUES
    (N'users', N'access_failed_count', 0),
    (N'users', N'lockout_end_at', 1),
    (N'users', N'password_changed_at', 1),
    (N'user_roles', N'assigned_by', 1),
    (N'user_roles', N'assigned_at', 0),
    (N'permissions', N'code', 0),
    (N'role_permissions', N'role_id', 0),
    (N'role_permissions', N'permission_id', 0),
    (N'role_permissions', N'assigned_at', 0),
    (N'refresh_tokens', N'token_hash', 0),
    (N'refresh_tokens', N'family_id', 0),
    (N'refresh_tokens', N'expires_at', 0),
    (N'password_reset_tokens', N'token_hash', 0),
    (N'password_reset_tokens', N'expires_at', 0),
    (N'audit_logs', N'action', 0),
    (N'audit_logs', N'entity_type', 0),
    (N'audit_logs', N'outcome', 0),
    (N'audit_logs', N'occurred_at', 0),
    (N'tags', N'name', 0),
    (N'tags', N'normalized_name', 0),
    (N'tags', N'tag_type', 0),
    (N'project_tags', N'project_id', 0),
    (N'project_tags', N'tag_id', 0),
    (N'project_tags', N'created_at', 0),
    (N'project_periods', N'period_type', 0),
    (N'team_members', N'team_id', 0),
    (N'team_members', N'academic_semester_id', 0),
    (N'team_members', N'user_id', 0),
    (N'rubrics', N'department_id', 1),
    (N'rubrics', N'academic_semester_id', 1),
    (N'rubrics', N'code', 0),
    (N'rubrics', N'version_number', 0),
    (N'rubrics', N'approval_status', 0),
    (N'rubrics', N'created_by', 0),
    (N'rubrics', N'approved_by', 1),
    (N'rubrics', N'approved_at', 1),
    (N'rubrics', N'row_version', 0),
    (N'rubric_criteria', N'rubric_id', 0),
    (N'rubric_criteria', N'criterion_id', 0),
    (N'rubric_criteria', N'weight_percent', 0),
    (N'rubric_criteria', N'max_score', 0),
    (N'evaluations', N'project_id', 0),
    (N'evaluations', N'evaluator_id', 0),
    (N'evaluations', N'rubric_id', 0),
    (N'evaluations', N'evidence_summary', 1),
    (N'evaluations', N'finalized_by', 1),
    (N'evaluations', N'finalized_at', 1),
    (N'evaluations', N'rounding_rule', 0),
    (N'evaluations', N'row_version', 0),
    (N'evaluation_details', N'evaluation_id', 0),
    (N'evaluation_details', N'rubric_criterion_id', 0),
    (N'evaluation_details', N'score', 0),
    (N'files', N'storage_path', 0),
    (N'files', N'file_size_bytes', 0);

IF EXISTS (
    SELECT 1
    FROM @required_columns rc
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + rc.table_name)
          AND c.name = rc.column_name
          AND c.is_nullable = rc.expected_nullable
    )
)
BEGIN
    SELECT rc.table_name, rc.column_name, rc.expected_nullable
    FROM @required_columns rc
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + rc.table_name)
          AND c.name = rc.column_name
          AND c.is_nullable = rc.expected_nullable
    );

    THROW 51013, 'Verification failed: important column missing or nullability is incorrect.', 1;
END;

IF COL_LENGTH(N'dbo.evaluation_criteria', N'weight_percent') IS NOT NULL
   OR COL_LENGTH(N'dbo.evaluation_criteria', N'max_score') IS NOT NULL
BEGIN
    THROW 51014, 'Verification failed: weight/max must belong to rubric_criteria.', 1;
END;

IF COL_LENGTH(N'dbo.evaluation_details', N'criterion_id') IS NOT NULL
BEGIN
    THROW 51015, 'Verification failed: evaluation_details must use rubric_criterion_id.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.files')
      AND t.name IN (N'varbinary', N'binary', N'image')
)
BEGIN
    THROW 51016, 'Verification failed: files table must store metadata/path only.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.refresh_tokens')
      AND c.name = N'token_hash'
      AND t.name = N'varbinary'
      AND c.max_length = 64
)
    THROW 51024, 'Verification failed: refresh token hash must be VARBINARY(64).', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.password_reset_tokens')
      AND c.name = N'token_hash'
      AND t.name = N'varbinary'
      AND c.max_length = 64
)
    THROW 51025, 'Verification failed: password-reset token hash must be VARBINARY(64).', 1;

IF COL_LENGTH(N'dbo.refresh_tokens', N'token') IS NOT NULL
   OR COL_LENGTH(N'dbo.password_reset_tokens', N'token') IS NOT NULL
BEGIN
    THROW 51026, 'Verification failed: raw security tokens must not be stored.', 1;
END;

/* =========================================================
   8. IMPORTANT DEFAULT CONSTRAINTS
   ========================================================= */

DECLARE @required_defaults TABLE (
    table_name SYSNAME NOT NULL,
    column_name SYSNAME NOT NULL,
    PRIMARY KEY (table_name, column_name)
);

INSERT INTO @required_defaults(table_name, column_name) VALUES
    (N'organizations', N'is_active'),
    (N'departments', N'is_active'),
    (N'majors', N'is_active'),
    (N'academic_semesters', N'status'),
    (N'project_periods', N'status'),
    (N'roles', N'is_system_role'),
    (N'permissions', N'is_system_permission'),
    (N'users', N'status'),
    (N'users', N'access_failed_count'),
    (N'audit_logs', N'outcome'),
    (N'tags', N'created_at'),
    (N'project_tags', N'created_at'),
    (N'teams', N'status'),
    (N'team_members', N'is_leader'),
    (N'team_invitations', N'status'),
    (N'projects', N'status'),
    (N'supervisor_profiles', N'is_available'),
    (N'supervisor_requests', N'status'),
    (N'supervisor_assignments', N'is_primary'),
    (N'milestones', N'status'),
    (N'milestones', N'sort_order'),
    (N'tasks', N'status'),
    (N'task_dependencies', N'dependency_type'),
    (N'progress_reports', N'status'),
    (N'meetings', N'status'),
    (N'deliverables', N'status'),
    (N'deliverable_versions', N'status'),
    (N'evaluation_criteria', N'is_active'),
    (N'rubrics', N'is_active'),
    (N'rubrics', N'version_number'),
    (N'rubrics', N'approval_status'),
    (N'rubric_criteria', N'sort_order'),
    (N'rubric_criteria', N'is_required'),
    (N'evaluations', N'status'),
    (N'evaluations', N'rounding_rule'),
    (N'notification_recipients', N'is_read');

IF EXISTS (
    SELECT 1
    FROM @required_defaults rd
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + rd.table_name)
          AND c.name = rd.column_name
          AND c.default_object_id <> 0
    )
)
BEGIN
    SELECT rd.table_name, rd.column_name AS missing_default
    FROM @required_defaults rd
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + rd.table_name)
          AND c.name = rd.column_name
          AND c.default_object_id <> 0
    );

    THROW 51017, 'Verification failed: required default constraint is missing.', 1;
END;

/* =========================================================
   9. CREATED_AT / UPDATED_AT EXPECTATIONS
   ========================================================= */

DECLARE @created_at_tables TABLE (table_name SYSNAME PRIMARY KEY);
INSERT INTO @created_at_tables(table_name) VALUES
    (N'organizations'),
    (N'departments'),
    (N'majors'),
    (N'academic_semesters'),
    (N'project_periods'),
    (N'roles'),
    (N'permissions'),
    (N'users'),
    (N'refresh_tokens'),
    (N'password_reset_tokens'),
    (N'tags'),
    (N'project_tags'),
    (N'teams'),
    (N'team_members'),
    (N'team_invitations'),
    (N'projects'),
    (N'project_majors'),
    (N'supervisor_profiles'),
    (N'supervisor_expertise'),
    (N'supervisor_requests'),
    (N'supervisor_assignments'),
    (N'milestones'),
    (N'tasks'),
    (N'task_dependencies'),
    (N'progress_reports'),
    (N'meetings'),
    (N'meeting_participants'),
    (N'deliverables'),
    (N'deliverable_versions'),
    (N'supervisor_feedback'),
    (N'files'),
    (N'evaluation_criteria'),
    (N'rubrics'),
    (N'rubric_criteria'),
    (N'evaluations'),
    (N'evaluation_details'),
    (N'notifications'),
    (N'notification_recipients');

IF EXISTS (
    SELECT 1
    FROM @created_at_tables t
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + t.table_name)
          AND c.name = N'created_at'
          AND c.is_nullable = 0
    )
)
BEGIN
    SELECT t.table_name AS missing_or_nullable_created_at
    FROM @created_at_tables t
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + t.table_name)
          AND c.name = N'created_at'
          AND c.is_nullable = 0
    );

    THROW 51018, 'Verification failed: required created_at column is missing or nullable.', 1;
END;

DECLARE @updated_at_tables TABLE (table_name SYSNAME PRIMARY KEY);
INSERT INTO @updated_at_tables(table_name) VALUES
    (N'organizations'),
    (N'departments'),
    (N'majors'),
    (N'academic_semesters'),
    (N'project_periods'),
    (N'roles'),
    (N'permissions'),
    (N'users'),
    (N'teams'),
    (N'team_members'),
    (N'team_invitations'),
    (N'projects'),
    (N'supervisor_profiles'),
    (N'supervisor_expertise'),
    (N'supervisor_requests'),
    (N'supervisor_assignments'),
    (N'milestones'),
    (N'tasks'),
    (N'progress_reports'),
    (N'meetings'),
    (N'meeting_participants'),
    (N'deliverables'),
    (N'deliverable_versions'),
    (N'supervisor_feedback'),
    (N'files'),
    (N'evaluation_criteria'),
    (N'rubrics'),
    (N'rubric_criteria'),
    (N'evaluations'),
    (N'evaluation_details'),
    (N'notifications'),
    (N'notification_recipients');

IF EXISTS (
    SELECT 1
    FROM @updated_at_tables t
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + t.table_name)
          AND c.name = N'updated_at'
          AND c.is_nullable = 0
    )
)
BEGIN
    SELECT t.table_name AS missing_or_nullable_updated_at
    FROM @updated_at_tables t
    WHERE NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.' + t.table_name)
          AND c.name = N'updated_at'
          AND c.is_nullable = 0
    );

    THROW 51019, 'Verification failed: required updated_at column is missing or nullable.', 1;
END;

/* =========================================================
   10. BUSINESS-STRUCTURE SANITY CHECKS
   ========================================================= */

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.projects')
      AND name = N'uq_projects_active_team'
      AND is_unique = 1
      AND is_disabled = 0
)
BEGIN
    THROW 51020, 'Verification failed: current design requires one active project per team.', 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.supervisor_profiles')
      AND name = N'uq_supervisor_profiles_user'
      AND is_unique = 1
      AND is_disabled = 0
)
BEGIN
    THROW 51021, 'Verification failed: supervisor profile must be unique per user.', 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.supervisor_assignments')
      AND name = N'ux_supervisor_assignments_one_primary_active'
      AND is_unique = 1
      AND has_filter = 1
      AND is_disabled = 0
)
BEGIN
    THROW 51022, 'Verification failed: active primary supervisor rule is missing.', 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.rubrics')
      AND name = N'ck_rubrics_scope'
      AND is_disabled = 0
)
BEGIN
    THROW 51023, 'Verification failed: rubric scope constraint is missing.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.refresh_tokens')
      AND name = N'ix_refresh_tokens_user_active'
      AND has_filter = 1
      AND is_disabled = 0
)
BEGIN
    THROW 51027, 'Verification failed: active refresh-token index is invalid or missing.', 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.password_reset_tokens')
      AND name = N'ix_password_reset_tokens_user_active'
      AND has_filter = 1
      AND is_disabled = 0
)
BEGIN
    THROW 51028, 'Verification failed: active password-reset-token index is invalid or missing.', 1;
END;

/* =========================================================
   11. SUMMARY
   ========================================================= */

SELECT
    (SELECT COUNT(*)
     FROM sys.tables
     WHERE schema_id = SCHEMA_ID(N'dbo')
       AND name IN (SELECT table_name FROM @expected_tables)
    ) AS required_tables_found,

    (SELECT COUNT(*)
     FROM dbo.roles
     WHERE code IN (N'ADMIN', N'DEPARTMENT_STAFF', N'LECTURER', N'STUDENT')
    ) AS required_roles_found,

    (SELECT COUNT(*)
     FROM sys.foreign_keys
     WHERE parent_object_id IN (
         SELECT OBJECT_ID(N'dbo.' + table_name) FROM @expected_tables
     )
    ) AS foreign_keys_found,

    (SELECT COUNT(*)
     FROM sys.check_constraints
     WHERE parent_object_id IN (
         SELECT OBJECT_ID(N'dbo.' + table_name) FROM @expected_tables
     )
    ) AS check_constraints_found,

    (SELECT COUNT(*)
     FROM sys.indexes
     WHERE object_id IN (
         SELECT OBJECT_ID(N'dbo.' + table_name) FROM @expected_tables
     )
       AND name IS NOT NULL
    ) AS indexes_found;

/* =========================================================
   11. PROJECT EXTENSIONS VALIDATION (BR-50 to BR-54)
   ========================================================= */

-- A. Verify projects table columns (problem_statement, expected_output, row_version)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.system_type_id = t.system_type_id AND c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.projects')
      AND (
          (c.name = N'problem_statement' AND t.name = N'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1)
          OR (c.name = N'expected_output' AND t.name = N'nvarchar' AND c.max_length = -1 AND c.is_nullable = 1)
          OR (c.name = N'row_version' AND t.name = N'timestamp' AND c.is_nullable = 0)
      )
    HAVING COUNT(*) = 3
)
BEGIN
    THROW 51030, 'Verification failed: required metadata columns (problem_statement, expected_output, row_version) are missing or misconfigured in dbo.projects.', 1;
END;

-- B. Verify domain and technology are NOT columns in projects table (since they are now normalized tags)
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.projects')
      AND name IN (N'domain', N'technology')
)
BEGIN
    THROW 51031, 'Verification failed: projects table should not contain domain or technology columns as they should be normalized tags.', 1;
END;

-- B2. Verify uq_projects_team constraint is dropped
IF EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.projects') AND name = N'uq_projects_team'
)
BEGIN
    THROW 51037, 'Verification failed: constraint uq_projects_team was not dropped from dbo.projects.', 1;
END;

-- B3. Verify unique filtered index uq_projects_active_team
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.projects')
      AND name = N'uq_projects_active_team'
      AND is_unique = 1
      AND has_filter = 1
      AND filter_definition LIKE N'%status%'
)
BEGIN
    THROW 51038, 'Verification failed: unique filtered index uq_projects_active_team is missing or misconfigured.', 1;
END;

-- C. Verify tags table columns and constraints
IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.system_type_id = t.system_type_id AND c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.tags')
      AND (
          (c.name = N'name' AND t.name = N'nvarchar' AND c.max_length = 200) -- max_length is in bytes, so NVARCHAR(100) is 200 bytes!
          OR (c.name = N'normalized_name' AND t.name = N'nvarchar' AND c.max_length = 200)
          OR (c.name = N'tag_type' AND t.name = N'nvarchar' AND c.max_length = 60)
      )
    HAVING COUNT(*) = 3
)
BEGIN
    THROW 51032, 'Verification failed: columns in dbo.tags table are missing or have incorrect type/length.', 1;
END;

-- C2. Verify check constraint ck_tags_type
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.tags')
      AND name = N'ck_tags_type'
      AND definition LIKE N'%DOMAIN%'
)
BEGIN
    THROW 51039, 'Verification failed: check constraint ck_tags_type on dbo.tags is missing or misconfigured.', 1;
END;

-- C3. Verify default constraint for created_at on dbo.tags
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.tags')
      AND name = N'df_tags_created_at'
)
BEGIN
    THROW 51040, 'Verification failed: default constraint df_tags_created_at on dbo.tags is missing.', 1;
END;

-- D. Verify unique constraint uq_tags_normalized_name_type
IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.tags')
      AND name = N'uq_tags_normalized_name_type'
      AND type = 'UQ'
)
BEGIN
    THROW 51033, 'Verification failed: unique constraint uq_tags_normalized_name_type is missing on dbo.tags.', 1;
END;

-- E. Verify project_tags composite primary key
IF NOT EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.project_tags')
      AND type = 'PK'
)
BEGIN
    THROW 51034, 'Verification failed: composite primary key pk_project_tags is missing on dbo.project_tags.', 1;
END;

-- E2. Verify composite primary key columns on dbo.project_tags
IF NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = OBJECT_ID(N'dbo.project_tags')
      AND ic.index_id = 1 -- Clustered PK index
      AND c.name = N'project_id'
)
OR NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = OBJECT_ID(N'dbo.project_tags')
      AND ic.index_id = 1
      AND c.name = N'tag_id'
)
BEGIN
    THROW 51041, 'Verification failed: composite primary key on dbo.project_tags must contain project_id and tag_id.', 1;
END;

-- F. Verify foreign keys fk_project_tags_project and fk_project_tags_tag
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.project_tags')
      AND name = N'fk_project_tags_project'
)
OR NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.project_tags')
      AND name = N'fk_project_tags_tag'
)
BEGIN
    THROW 51035, 'Verification failed: foreign keys on dbo.project_tags are missing.', 1;
END;

-- F2. Verify FK delete actions on dbo.project_tags
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE object_id = OBJECT_ID(N'dbo.fk_project_tags_project')
      AND delete_referential_action = 1 -- CASCADE
)
OR NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE object_id = OBJECT_ID(N'dbo.fk_project_tags_tag')
      AND delete_referential_action = 0 -- NO ACTION
)
BEGIN
    THROW 51042, 'Verification failed: foreign key delete referential actions on dbo.project_tags are incorrect.', 1;
END;

-- F3. Verify default constraint for created_at on dbo.project_tags
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.project_tags')
      AND name = N'df_project_tags_created_at'
)
BEGIN
    THROW 51043, 'Verification failed: default constraint df_project_tags_created_at on dbo.project_tags is missing.', 1;
END;

-- G. Verify composite search index ix_project_tags_tag_project
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.project_tags')
      AND name = N'ix_project_tags_tag_project'
      AND is_unique = 0
)
BEGIN
    THROW 51036, 'Verification failed: composite index ix_project_tags_tag_project is missing on dbo.project_tags.', 1;
END;

-- G2. Verify index columns for ix_project_tags_tag_project
IF NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = OBJECT_ID(N'dbo.project_tags')
      AND ic.index_id = INDEXPROPERTY(OBJECT_ID(N'dbo.project_tags'), N'ix_project_tags_tag_project', 'IndexId')
      AND ic.key_ordinal = 1
      AND c.name = N'tag_id'
)
OR NOT EXISTS (
    SELECT 1 FROM sys.index_columns ic
    JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
    WHERE ic.object_id = OBJECT_ID(N'dbo.project_tags')
      AND ic.index_id = INDEXPROPERTY(OBJECT_ID(N'dbo.project_tags'), N'ix_project_tags_tag_project', 'IndexId')
      AND ic.key_ordinal = 2
      AND c.name = N'project_id'
)
BEGIN
    THROW 51044, 'Verification failed: index ix_project_tags_tag_project must have tag_id as first key column and project_id as second.', 1;
END;

PRINT N'AI-PMS verification checks passed.';
GO
