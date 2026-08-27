/*
 AI-PMS transactional smoke test.
 Run AFTER schema.sql and seed.sql.

 It exercises one end-to-end non-AI project flow and rolls everything back.

 Coverage:
 - Academic structure
 - Roles / users / permissions
 - Refresh-token rotation + password-reset lifecycle
 - Persistent audit trail
 - Team invitation + membership
 - Multi-major team
 - Project + multi-major project
 - Project status history
 - Supervisor profile / expertise / request / assignment
 - Project activation after accepted supervisor assignment
 - Milestone / multiple tasks / multiple assignees
 - Task dependency + task status history
 - Progress report
 - Meeting + participants
 - Deliverable + version + file metadata
 - Supervisor feedback
 - Rubric-based evaluation + evaluation detail
 - Notification + recipients

 No permanent business data is left behind because the whole test runs
 inside a transaction and is rolled back after all assertions pass.
*/

USE [AI_PMS];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    /* =========================================================
       1. VARIABLE DECLARATIONS
       ========================================================= */

    DECLARE
        @org_id BIGINT,
        @dept_id BIGINT,
        @major_se_id BIGINT,
        @major_is_id BIGINT,
        @semester_id BIGINT,
        @period_id BIGINT,

        @student1_id BIGINT,
        @student2_id BIGINT,
        @lecturer_id BIGINT,

        @student_role_id BIGINT,
        @lecturer_role_id BIGINT,

        @permission_id BIGINT,
        @refresh_token_id BIGINT,
        @rotated_refresh_token_id BIGINT,
        @password_reset_token_id BIGINT,
        @audit_log_id BIGINT,
        @token_family_id UNIQUEIDENTIFIER,

        @team_id BIGINT,
        @invitation_id BIGINT,

        @project_id BIGINT,

        @supervisor_profile_id BIGINT,
        @supervisor_request_id BIGINT,
        @assignment_id BIGINT,

        @milestone_id BIGINT,

        @task_id BIGINT,
        @task2_id BIGINT,

        @progress_report_id BIGINT,

        @meeting_id BIGINT,

        @deliverable_id BIGINT,
        @deliverable_version_id BIGINT,
        @file_id BIGINT,

        @feedback_id BIGINT,

        @criterion_id BIGINT,
        @rubric_id BIGINT,
        @rubric_criterion_id BIGINT,
        @evaluation_id BIGINT,
        @evaluation_detail_id BIGINT,

        @notification_id BIGINT;

    /* =========================================================
       2. REQUIRED SEEDED ROLES
       ========================================================= */

    SELECT @student_role_id = id
    FROM dbo.roles
    WHERE code = N'STUDENT';

    SELECT @lecturer_role_id = id
    FROM dbo.roles
    WHERE code = N'LECTURER';

    IF @student_role_id IS NULL OR @lecturer_role_id IS NULL
        THROW 51100,
            'Smoke test requires seed.sql to be executed first.',
            1;

    /* =========================================================
       3. ACADEMIC STRUCTURE
       ========================================================= */

    INSERT INTO dbo.organizations(code, name)
    VALUES (
        N'__SMOKE_ORG__',
        N'AI-PMS Smoke Test University'
    );

    SET @org_id = SCOPE_IDENTITY();


    INSERT INTO dbo.departments(
        organization_id,
        code,
        name
    )
    VALUES (
        @org_id,
        N'__SMOKE_IT__',
        N'Information Technology'
    );

    SET @dept_id = SCOPE_IDENTITY();


    INSERT INTO dbo.majors(
        department_id,
        code,
        name
    )
    VALUES (
        @dept_id,
        N'__SMOKE_SE__',
        N'Software Engineering'
    );

    SET @major_se_id = SCOPE_IDENTITY();


    INSERT INTO dbo.majors(
        department_id,
        code,
        name
    )
    VALUES (
        @dept_id,
        N'__SMOKE_IS__',
        N'Information Systems'
    );

    SET @major_is_id = SCOPE_IDENTITY();


    INSERT INTO dbo.academic_semesters(
        organization_id,
        code,
        name,
        start_date,
        end_date,
        status
    )
    VALUES (
        @org_id,
        N'__SMOKE_FA26__',
        N'Fall 2026 Smoke Test',
        '2026-09-01',
        '2026-12-31',
        N'ACTIVE'
    );

    SET @semester_id = SCOPE_IDENTITY();


    INSERT INTO dbo.project_periods(
        academic_semester_id,
        code,
        name,
        period_type,
        start_at,
        end_at,
        status
    )
    VALUES (
        @semester_id,
        N'__SMOKE_EXEC__',
        N'Execution Period',
        N'EXECUTION',
        '2026-09-01T00:00:00',
        '2026-12-31T23:59:59',
        N'ACTIVE'
    );

    SET @period_id = SCOPE_IDENTITY();

    /* =========================================================
       4. USERS + USER ROLES
       ========================================================= */

    INSERT INTO dbo.users(
        major_id,
        email,
        password_hash,
        full_name,
        phone,
        student_code
    )
    VALUES (
        @major_se_id,
        N'smoke.student1@example.invalid',
        N'not-a-real-password-hash',
        N'Smoke Student Leader',
        N'000000001',
        N'__SMOKE_S1__'
    );

    SET @student1_id = SCOPE_IDENTITY();


    INSERT INTO dbo.users(
        major_id,
        email,
        password_hash,
        full_name,
        phone,
        student_code
    )
    VALUES (
        @major_is_id,
        N'smoke.student2@example.invalid',
        N'not-a-real-password-hash',
        N'Smoke Student Member',
        N'000000002',
        N'__SMOKE_S2__'
    );

    SET @student2_id = SCOPE_IDENTITY();


    INSERT INTO dbo.users(
        department_id,
        email,
        password_hash,
        full_name,
        phone,
        employee_code,
        title
    )
    VALUES (
        @dept_id,
        N'smoke.lecturer@example.invalid',
        N'not-a-real-password-hash',
        N'Smoke Lecturer',
        N'000000003',
        N'__SMOKE_L1__',
        N'Lecturer'
    );

    SET @lecturer_id = SCOPE_IDENTITY();


    INSERT INTO dbo.user_roles(
        user_id,
        role_id,
        assigned_by
    )
    VALUES
        (@student1_id, @student_role_id, @lecturer_id),
        (@student2_id, @student_role_id, @lecturer_id),
        (@lecturer_id, @lecturer_role_id, @lecturer_id);

    /* =========================================================
       4A. BE-10 ACCOUNT SECURITY
       ========================================================= */

    INSERT INTO dbo.permissions(
        code,
        name,
        description
    )
    VALUES (
        N'__SMOKE_PROJECT_READ__',
        N'Smoke project read',
        N'Transactional smoke-test permission.'
    );

    SET @permission_id = SCOPE_IDENTITY();

    INSERT INTO dbo.role_permissions(
        role_id,
        permission_id,
        assigned_by
    )
    VALUES (
        @lecturer_role_id,
        @permission_id,
        @lecturer_id
    );

    SET @token_family_id = NEWID();

    INSERT INTO dbo.refresh_tokens(
        user_id,
        token_hash,
        family_id,
        expires_at,
        created_by_ip,
        user_agent
    )
    VALUES (
        @lecturer_id,
        HASHBYTES('SHA2_512', N'__SMOKE_REFRESH_TOKEN_1__'),
        @token_family_id,
        DATEADD(DAY, 30, SYSUTCDATETIME()),
        N'127.0.0.1',
        N'AI-PMS smoke test'
    );

    SET @refresh_token_id = SCOPE_IDENTITY();

    INSERT INTO dbo.refresh_tokens(
        user_id,
        token_hash,
        family_id,
        expires_at,
        created_by_ip,
        user_agent
    )
    VALUES (
        @lecturer_id,
        HASHBYTES('SHA2_512', N'__SMOKE_REFRESH_TOKEN_2__'),
        @token_family_id,
        DATEADD(DAY, 30, SYSUTCDATETIME()),
        N'127.0.0.1',
        N'AI-PMS smoke test'
    );

    SET @rotated_refresh_token_id = SCOPE_IDENTITY();

    UPDATE dbo.refresh_tokens
    SET revoked_at = SYSUTCDATETIME(),
        revoked_by_ip = N'127.0.0.1',
        replaced_by_token_id = @rotated_refresh_token_id,
        reuse_detected_at = SYSUTCDATETIME()
    WHERE id = @refresh_token_id;

    INSERT INTO dbo.password_reset_tokens(
        user_id,
        token_hash,
        expires_at,
        requested_by_ip
    )
    VALUES (
        @student1_id,
        HASHBYTES('SHA2_512', N'__SMOKE_PASSWORD_RESET_TOKEN__'),
        DATEADD(MINUTE, 30, SYSUTCDATETIME()),
        N'127.0.0.1'
    );

    SET @password_reset_token_id = SCOPE_IDENTITY();

    UPDATE dbo.password_reset_tokens
    SET used_at = SYSUTCDATETIME()
    WHERE id = @password_reset_token_id;

    INSERT INTO dbo.audit_logs(
        actor_user_id,
        action,
        entity_type,
        entity_id,
        outcome,
        correlation_id,
        ip_address,
        user_agent,
        details_json
    )
    VALUES (
        @lecturer_id,
        N'ROLE.PERMISSION_ASSIGNED',
        N'ROLE',
        CONVERT(NVARCHAR(100), @lecturer_role_id),
        N'SUCCESS',
        NEWID(),
        N'127.0.0.1',
        N'AI-PMS smoke test',
        N'{"permission":"__SMOKE_PROJECT_READ__"}'
    );

    SET @audit_log_id = SCOPE_IDENTITY();

    /* =========================================================
       5. TEAM + INVITATION + MEMBERS
       ========================================================= */

    INSERT INTO dbo.teams(
        academic_semester_id,
        code,
        name,
        status,
        created_by
    )
    VALUES (
        @semester_id,
        N'__SMOKE_TEAM__',
        N'AI-PMS Smoke Team',
        N'FORMING',
        @student1_id
    );

    SET @team_id = SCOPE_IDENTITY();


    /* Leader is added first. */
    INSERT INTO dbo.team_members(
        team_id,
        academic_semester_id,
        user_id,
        is_leader
    )
    VALUES (
        @team_id,
        @semester_id,
        @student1_id,
        1
    );


    /* Test team invitation flow. */
    INSERT INTO dbo.team_invitations(
        team_id,
        invited_user_id,
        invited_by,
        status,
        message,
        responded_at
    )
    VALUES (
        @team_id,
        @student2_id,
        @student1_id,
        N'ACCEPTED',
        N'Smoke test team invitation.',
        SYSUTCDATETIME()
    );

    SET @invitation_id = SCOPE_IDENTITY();


    /* Accepted member joins the team. */
    INSERT INTO dbo.team_members(
        team_id,
        academic_semester_id,
        user_id,
        is_leader
    )
    VALUES (
        @team_id,
        @semester_id,
        @student2_id,
        0
    );


    UPDATE dbo.teams
    SET
        status = N'ELIGIBLE',
        updated_at = SYSUTCDATETIME()
    WHERE id = @team_id;

    /* =========================================================
       6. PROJECT + MULTI-MAJOR + STATUS HISTORY
       ========================================================= */

    INSERT INTO dbo.projects(
        team_id,
        code,
        title,
        description,
        objectives,
        status,
        created_by,
        submitted_at,
        approved_at
    )
    VALUES (
        @team_id,
        N'__SMOKE_PROJECT__',
        N'AI-PMS Smoke Test Project',
        N'Transactional test project.',
        N'Validate the initial schema relationships.',
        N'SUPERVISOR_PENDING',
        @student1_id,
        SYSUTCDATETIME(),
        SYSUTCDATETIME()
    );

    SET @project_id = SCOPE_IDENTITY();


    INSERT INTO dbo.project_majors(
        project_id,
        major_id
    )
    VALUES
        (@project_id, @major_se_id),
        (@project_id, @major_is_id);


    DECLARE @tag_domain_id BIGINT, @tag_tech_id BIGINT, @tag_keyword_id BIGINT;

    -- Get or insert tag Domain
    IF EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'SOFTWARE_ENGINEERING' AND tag_type = N'DOMAIN')
    BEGIN
        SELECT @tag_domain_id = id FROM dbo.tags WHERE normalized_name = N'SOFTWARE_ENGINEERING' AND tag_type = N'DOMAIN';
    END
    ELSE
    BEGIN
        INSERT INTO dbo.tags (name, normalized_name, tag_type)
        VALUES (N'Software Engineering', N'SOFTWARE_ENGINEERING', N'DOMAIN');
        SET @tag_domain_id = SCOPE_IDENTITY();
    END

    -- Get or insert tag Technology
    IF EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'REACT_NATIVE' AND tag_type = N'TECHNOLOGY')
    BEGIN
        SELECT @tag_tech_id = id FROM dbo.tags WHERE normalized_name = N'REACT_NATIVE' AND tag_type = N'TECHNOLOGY';
    END
    ELSE
    BEGIN
        INSERT INTO dbo.tags (name, normalized_name, tag_type)
        VALUES (N'React Native', N'REACT_NATIVE', N'TECHNOLOGY');
        SET @tag_tech_id = SCOPE_IDENTITY();
    END

    -- Get or insert tag Keyword
    IF EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'AI_PMS' AND tag_type = N'KEYWORD')
    BEGIN
        SELECT @tag_keyword_id = id FROM dbo.tags WHERE normalized_name = N'AI_PMS' AND tag_type = N'KEYWORD';
    END
    ELSE
    BEGIN
        INSERT INTO dbo.tags (name, normalized_name, tag_type)
        VALUES (N'AI_PMS', N'AI_PMS', N'KEYWORD');
        SET @tag_keyword_id = SCOPE_IDENTITY();
    END

    INSERT INTO dbo.project_tags (project_id, tag_id)
    VALUES
        (@project_id, @tag_domain_id),
        (@project_id, @tag_tech_id),
        (@project_id, @tag_keyword_id);

    -- Assert metadata/tag đã lưu đúng
    IF (SELECT COUNT(*) FROM dbo.project_tags WHERE project_id = @project_id) <> 3
    BEGIN
        THROW 51090, 'Smoke test failed: project tags count is not 3.', 1;
    END


    INSERT INTO dbo.project_status_history(
        project_id,
        old_status,
        new_status,
        changed_by,
        reason
    )
    VALUES (
        @project_id,
        N'APPROVED',
        N'SUPERVISOR_PENDING',
        @student1_id,
        N'Smoke test transition'
    );

    /* =========================================================
       7. SUPERVISOR WORKFLOW
       ========================================================= */

    INSERT INTO dbo.supervisor_profiles(
        user_id,
        bio,
        max_active_projects,
        is_available
    )
    VALUES (
        @lecturer_id,
        N'Smoke supervisor profile',
        5,
        1
    );

    SET @supervisor_profile_id = SCOPE_IDENTITY();


    INSERT INTO dbo.supervisor_expertise(
        supervisor_profile_id,
        expertise_name,
        proficiency_level
    )
    VALUES (
        @supervisor_profile_id,
        N'Software Engineering',
        N'ADVANCED'
    );


    INSERT INTO dbo.supervisor_requests(
        project_id,
        supervisor_profile_id,
        requested_by,
        status,
        request_message,
        response_message,
        responded_at
    )
    VALUES (
        @project_id,
        @supervisor_profile_id,
        @student1_id,
        N'ACCEPTED',
        N'Please supervise this project.',
        N'Accepted for smoke testing.',
        SYSUTCDATETIME()
    );

    SET @supervisor_request_id = SCOPE_IDENTITY();


    INSERT INTO dbo.supervisor_assignments(
        project_id,
        supervisor_profile_id,
        supervisor_request_id,
        is_primary
    )
    VALUES (
        @project_id,
        @supervisor_profile_id,
        @supervisor_request_id,
        1
    );

    SET @assignment_id = SCOPE_IDENTITY();


    /* Accepted supervisor assignment activates the project. */
    UPDATE dbo.projects
    SET
        status = N'ACTIVE',
        updated_at = SYSUTCDATETIME()
    WHERE id = @project_id;


    INSERT INTO dbo.project_status_history(
        project_id,
        old_status,
        new_status,
        changed_by,
        reason
    )
    VALUES (
        @project_id,
        N'SUPERVISOR_PENDING',
        N'ACTIVE',
        @lecturer_id,
        N'Supervisor accepted assignment during smoke test'
    );

    /* =========================================================
       8. MILESTONE + TASKS
       ========================================================= */

    INSERT INTO dbo.milestones(
        project_id,
        title,
        description,
        start_date,
        due_date,
        status,
        sort_order,
        created_by
    )
    VALUES (
        @project_id,
        N'Milestone 1',
        N'Initial implementation milestone',
        '2026-09-01',
        '2026-09-30',
        N'IN_PROGRESS',
        1,
        @student1_id
    );

    SET @milestone_id = SCOPE_IDENTITY();


    INSERT INTO dbo.tasks(
        milestone_id,
        title,
        description,
        status,
        priority,
        start_at,
        due_at,
        created_by
    )
    VALUES (
        @milestone_id,
        N'Design database',
        N'Build SQL Server schema',
        N'IN_PROGRESS',
        N'HIGH',
        '2026-09-01T08:00:00',
        '2026-09-10T23:59:59',
        @student1_id
    );

    SET @task_id = SCOPE_IDENTITY();


    INSERT INTO dbo.tasks(
        milestone_id,
        title,
        description,
        status,
        priority,
        start_at,
        due_at,
        created_by
    )
    VALUES (
        @milestone_id,
        N'Implement database access',
        N'Implement backend database integration after schema design is completed.',
        N'TODO',
        N'MEDIUM',
        '2026-09-11T08:00:00',
        '2026-09-18T23:59:59',
        @student1_id
    );

    SET @task2_id = SCOPE_IDENTITY();


    /* Multiple assignees on one task. */
    INSERT INTO dbo.task_assignees(
        task_id,
        user_id,
        assigned_by
    )
    VALUES
        (@task_id, @student1_id, @student1_id),
        (@task_id, @student2_id, @student1_id);


    /* Second task assigned to member. */
    INSERT INTO dbo.task_assignees(
        task_id,
        user_id,
        assigned_by
    )
    VALUES (
        @task2_id,
        @student2_id,
        @student1_id
    );


    /* Task dependency: Task 2 can start only after Task 1 finishes. */
    INSERT INTO dbo.task_dependencies(
        task_id,
        depends_on_task_id,
        dependency_type
    )
    VALUES (
        @task2_id,
        @task_id,
        N'FINISH_TO_START'
    );


    INSERT INTO dbo.task_status_history(
        task_id,
        old_status,
        new_status,
        changed_by,
        reason
    )
    VALUES (
        @task_id,
        N'TODO',
        N'IN_PROGRESS',
        @student1_id,
        N'Smoke test transition'
    );

    /* =========================================================
       9. PROGRESS REPORT
       ========================================================= */

    INSERT INTO dbo.progress_reports(
        project_id,
        submitted_by,
        report_type,
        period_start,
        period_end,
        summary,
        completed_work,
        planned_work,
        issues_and_risks,
        status,
        submitted_at
    )
    VALUES (
        @project_id,
        @student1_id,
        N'WEEKLY',
        '2026-09-01',
        '2026-09-07',
        N'Initial progress is on track.',
        N'Database design started.',
        N'Complete core schema.',
        N'No critical blocker identified.',
        N'SUBMITTED',
        SYSUTCDATETIME()
    );

    SET @progress_report_id = SCOPE_IDENTITY();

    /* =========================================================
       10. MEETING + PARTICIPANTS
       ========================================================= */

    INSERT INTO dbo.meetings(
        project_id,
        title,
        agenda,
        meeting_notes,
        start_at,
        end_at,
        location,
        status,
        created_by
    )
    VALUES (
        @project_id,
        N'Weekly supervisor meeting',
        N'Review progress',
        N'Smoke meeting notes.',
        '2026-09-08T09:00:00',
        '2026-09-08T10:00:00',
        N'Online',
        N'COMPLETED',
        @lecturer_id
    );

    SET @meeting_id = SCOPE_IDENTITY();


    INSERT INTO dbo.meeting_participants(
        meeting_id,
        user_id,
        attendance_status
    )
    VALUES
        (@meeting_id, @lecturer_id, N'ATTENDED'),
        (@meeting_id, @student1_id, N'ATTENDED'),
        (@meeting_id, @student2_id, N'ATTENDED');

    /* =========================================================
       11. DELIVERABLE + VERSION + FILE METADATA
       ========================================================= */

    INSERT INTO dbo.deliverables(
        project_id,
        milestone_id,
        title,
        description,
        deliverable_type,
        due_at,
        status,
        created_by
    )
    VALUES (
        @project_id,
        @milestone_id,
        N'Database Design',
        N'Initial database package',
        N'DOCUMENT',
        '2026-09-15T23:59:59',
        N'SUBMITTED',
        @student1_id
    );

    SET @deliverable_id = SCOPE_IDENTITY();


    INSERT INTO dbo.deliverable_versions(
        deliverable_id,
        version_number,
        submitted_by,
        submission_note,
        status
    )
    VALUES (
        @deliverable_id,
        1,
        @student1_id,
        N'Initial version',
        N'SUBMITTED'
    );

    SET @deliverable_version_id = SCOPE_IDENTITY();


    INSERT INTO dbo.files(
        uploaded_by,
        deliverable_version_id,
        original_file_name,
        stored_file_name,
        storage_path,
        file_url,
        mime_type,
        file_size_bytes,
        checksum_sha256
    )
    VALUES (
        @student1_id,
        @deliverable_version_id,
        N'database-design.pdf',
        N'0001-database-design.pdf',
        N'/projects/smoke/database-design.pdf',
        N'https://example.invalid/projects/smoke/database-design.pdf',
        N'application/pdf',
        1024,
        'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
    );

    SET @file_id = SCOPE_IDENTITY();

    /* =========================================================
       12. SUPERVISOR FEEDBACK
       ========================================================= */

    INSERT INTO dbo.supervisor_feedback(
        project_id,
        supervisor_assignment_id,
        deliverable_version_id,
        feedback_text
    )
    VALUES (
        @project_id,
        @assignment_id,
        @deliverable_version_id,
        N'Initial database structure is acceptable for smoke testing.'
    );

    SET @feedback_id = SCOPE_IDENTITY();

    /* =========================================================
       13. RUBRIC-BASED EVALUATION + DETAIL
       ========================================================= */

    INSERT INTO dbo.evaluation_criteria(
        code,
        name,
        description
    )
    VALUES (
        N'__SMOKE_TECH__',
        N'Technical Quality',
        N'Smoke criterion'
    );

    SET @criterion_id = SCOPE_IDENTITY();


    INSERT INTO dbo.rubrics(
        department_id,
        academic_semester_id,
        code,
        name,
        description,
        version_number,
        approval_status,
        is_active,
        created_by,
        approved_by,
        approved_at
    )
    VALUES (
        @dept_id,
        @semester_id,
        N'__SMOKE_RUBRIC__',
        N'AI-PMS Smoke Test Rubric',
        N'Rubric used only inside the transactional smoke test.',
        1,
        N'APPROVED',
        1,
        @lecturer_id,
        @lecturer_id,
        SYSUTCDATETIME()
    );

    SET @rubric_id = SCOPE_IDENTITY();


    INSERT INTO dbo.rubric_criteria(
        rubric_id,
        criterion_id,
        weight_percent,
        max_score,
        sort_order,
        is_required
    )
    VALUES (
        @rubric_id,
        @criterion_id,
        100.00,
        10.00,
        1,
        1
    );

    SET @rubric_criterion_id = SCOPE_IDENTITY();


    INSERT INTO dbo.evaluations(
        project_id,
        evaluator_id,
        rubric_id,
        evaluation_type,
        status,
        total_score,
        comments,
        evidence_summary,
        evaluated_at,
        finalized_by,
        finalized_at,
        rounding_rule
    )
    VALUES (
        @project_id,
        @lecturer_id,
        @rubric_id,
        N'SUPERVISOR',
        N'FINALIZED',
        9.00,
        N'Smoke evaluation',
        N'Smoke-test evidence summary',
        SYSUTCDATETIME(),
        @lecturer_id,
        SYSUTCDATETIME(),
        N'AWAY_FROM_ZERO_2DP'
    );

    SET @evaluation_id = SCOPE_IDENTITY();


    INSERT INTO dbo.evaluation_details(
        evaluation_id,
        rubric_criterion_id,
        score,
        comments
    )
    VALUES (
        @evaluation_id,
        @rubric_criterion_id,
        9.00,
        N'Passed smoke test'
    );

    SET @evaluation_detail_id = SCOPE_IDENTITY();

    /* =========================================================
       14. NOTIFICATION + RECIPIENTS
       ========================================================= */

    INSERT INTO dbo.notifications(
        created_by,
        notification_type,
        title,
        content,
        related_entity_type,
        related_entity_id
    )
    VALUES (
        @lecturer_id,
        N'PROJECT_UPDATE',
        N'Smoke notification',
        N'The smoke project has an update.',
        N'PROJECT',
        @project_id
    );

    SET @notification_id = SCOPE_IDENTITY();


    INSERT INTO dbo.notification_recipients(
        notification_id,
        user_id
    )
    VALUES
        (@notification_id, @student1_id),
        (@notification_id, @student2_id);

    /* =========================================================
       15. ASSERTIONS
       ========================================================= */

    /* Account lockout state starts from a safe default. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.users
        WHERE id = @student1_id
          AND access_failed_count = 0
          AND lockout_end_at IS NULL
    )
        THROW 51122,
            'Smoke test failed: account lockout defaults are invalid.',
            1;

    /* Permission assignment records both relationship and actor. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.role_permissions rp
        JOIN dbo.permissions p ON p.id = rp.permission_id
        WHERE rp.role_id = @lecturer_role_id
          AND rp.permission_id = @permission_id
          AND rp.assigned_by = @lecturer_id
          AND p.is_system_permission = 0
    )
        THROW 51123,
            'Smoke test failed: role permission relationship is invalid.',
            1;

    /* Refresh-token rotation preserves family and replacement history. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.refresh_tokens old_token
        JOIN dbo.refresh_tokens new_token
          ON new_token.id = old_token.replaced_by_token_id
        WHERE old_token.id = @refresh_token_id
          AND new_token.id = @rotated_refresh_token_id
          AND old_token.family_id = @token_family_id
          AND new_token.family_id = @token_family_id
          AND old_token.revoked_at IS NOT NULL
          AND old_token.reuse_detected_at IS NOT NULL
          AND DATALENGTH(old_token.token_hash) = 64
          AND DATALENGTH(new_token.token_hash) = 64
    )
        THROW 51124,
            'Smoke test failed: refresh-token rotation history is invalid.',
            1;

    /* Password-reset token is hashed and can be marked as consumed. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.password_reset_tokens
        WHERE id = @password_reset_token_id
          AND user_id = @student1_id
          AND used_at IS NOT NULL
          AND DATALENGTH(token_hash) = 64
    )
        THROW 51125,
            'Smoke test failed: password-reset token lifecycle is invalid.',
            1;

    /* Security audit stores actor, outcome and valid JSON context. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.audit_logs
        WHERE id = @audit_log_id
          AND actor_user_id = @lecturer_id
          AND action = N'ROLE.PERMISSION_ASSIGNED'
          AND outcome = N'SUCCESS'
          AND ISJSON(details_json) = 1
    )
        THROW 51126,
            'Smoke test failed: persistent security audit is invalid.',
            1;

    /* Team invitation was stored correctly. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.team_invitations
        WHERE id = @invitation_id
          AND team_id = @team_id
          AND invited_user_id = @student2_id
          AND invited_by = @student1_id
          AND status = N'ACCEPTED'
    )
        THROW 51101,
            'Smoke test failed: team invitation relationship was not stored correctly.',
            1;


    /* Team has exactly two active members. */
    IF (
        SELECT COUNT(*)
        FROM dbo.team_members
        WHERE team_id = @team_id
          AND academic_semester_id = @semester_id
          AND left_at IS NULL
    ) <> 2
        THROW 51102,
            'Smoke test failed: expected two active team members.',
            1;


    /* Team is multi-major. */
    IF (
        SELECT COUNT(DISTINCT u.major_id)
        FROM dbo.team_members tm
        JOIN dbo.users u
            ON u.id = tm.user_id
        WHERE tm.team_id = @team_id
          AND tm.academic_semester_id = @semester_id
          AND tm.left_at IS NULL
    ) <> 2
        THROW 51103,
            'Smoke test failed: multi-major team relationship was not stored as expected.',
            1;


    /* Exactly one active team leader exists. */
    IF (
        SELECT COUNT(*)
        FROM dbo.team_members
        WHERE team_id = @team_id
          AND academic_semester_id = @semester_id
          AND is_leader = 1
          AND left_at IS NULL
    ) <> 1
        THROW 51104,
            'Smoke test failed: team leader relationship is invalid.',
            1;


    /* Project has two majors. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.projects p
        JOIN dbo.project_majors pm
            ON pm.project_id = p.id
        WHERE p.id = @project_id
        GROUP BY p.id
        HAVING COUNT(pm.id) = 2
    )
        THROW 51105,
            'Smoke test failed: multi-major project relationship was not stored as expected.',
            1;


    /* Accepted supervisor request must be connected to the assignment. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.supervisor_assignments sa
        JOIN dbo.supervisor_requests sr
            ON sr.id = sa.supervisor_request_id
        WHERE sa.id = @assignment_id
          AND sa.project_id = @project_id
          AND sr.status = N'ACCEPTED'
    )
        THROW 51106,
            'Smoke test failed: accepted supervisor request/assignment relationship is invalid.',
            1;


    /* Project became ACTIVE after accepted assignment. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.projects
        WHERE id = @project_id
          AND status = N'ACTIVE'
    )
        THROW 51107,
            'Smoke test failed: project did not become ACTIVE after supervisor assignment.',
            1;


    /* Project ACTIVE transition history was stored. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.project_status_history
        WHERE project_id = @project_id
          AND old_status = N'SUPERVISOR_PENDING'
          AND new_status = N'ACTIVE'
    )
        THROW 51108,
            'Smoke test failed: project ACTIVE status history was not stored.',
            1;


    /* One milestone has multiple tasks. */
    IF (
        SELECT COUNT(*)
        FROM dbo.tasks
        WHERE milestone_id = @milestone_id
    ) <> 2
        THROW 51109,
            'Smoke test failed: milestone/task one-to-many relationship is invalid.',
            1;


    /* First task has two assignees. */
    IF (
        SELECT COUNT(*)
        FROM dbo.task_assignees
        WHERE task_id = @task_id
    ) <> 2
        THROW 51110,
            'Smoke test failed: multiple task assignees were not stored as expected.',
            1;


    /* Task dependency was stored correctly. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.task_dependencies
        WHERE task_id = @task2_id
          AND depends_on_task_id = @task_id
          AND dependency_type = N'FINISH_TO_START'
    )
        THROW 51111,
            'Smoke test failed: task dependency relationship was not stored correctly.',
            1;


    /* Task status history was stored. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.task_status_history
        WHERE task_id = @task_id
          AND old_status = N'TODO'
          AND new_status = N'IN_PROGRESS'
    )
        THROW 51112,
            'Smoke test failed: task status history was not stored correctly.',
            1;


    /* Progress report belongs to project and submitting student. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.progress_reports
        WHERE id = @progress_report_id
          AND project_id = @project_id
          AND submitted_by = @student1_id
          AND report_type = N'WEEKLY'
    )
        THROW 51113,
            'Smoke test failed: progress report relationship is invalid.',
            1;


    /* Meeting has lecturer + 2 students. */
    IF (
        SELECT COUNT(*)
        FROM dbo.meeting_participants
        WHERE meeting_id = @meeting_id
    ) <> 3
        THROW 51114,
            'Smoke test failed: meeting participants were not stored as expected.',
            1;


    /* Deliverable version belongs to deliverable. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.deliverable_versions
        WHERE id = @deliverable_version_id
          AND deliverable_id = @deliverable_id
          AND version_number = 1
    )
        THROW 51115,
            'Smoke test failed: deliverable version relationship is invalid.',
            1;


    /* File metadata points to deliverable version and does not require binary content. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.files
        WHERE id = @file_id
          AND deliverable_version_id = @deliverable_version_id
          AND uploaded_by = @student1_id
          AND storage_path IS NOT NULL
          AND file_size_bytes = 1024
    )
        THROW 51116,
            'Smoke test failed: file metadata relationship is invalid.',
            1;


    /* Supervisor feedback is connected to assignment + deliverable version. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.supervisor_feedback
        WHERE id = @feedback_id
          AND project_id = @project_id
          AND supervisor_assignment_id = @assignment_id
          AND deliverable_version_id = @deliverable_version_id
    )
        THROW 51117,
            'Smoke test failed: supervisor feedback relationship is invalid.',
            1;


    /* Rubric is scoped correctly and contains the configured criterion. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.rubrics r
        JOIN dbo.rubric_criteria rc ON rc.rubric_id = r.id
        WHERE r.id = @rubric_id
          AND r.department_id = @dept_id
          AND r.academic_semester_id = @semester_id
          AND r.version_number = 1
          AND r.approval_status = N'APPROVED'
          AND r.approved_by = @lecturer_id
          AND r.approved_at IS NOT NULL
          AND rc.id = @rubric_criterion_id
          AND rc.criterion_id = @criterion_id
          AND rc.weight_percent = 100.00
          AND rc.max_score = 10.00
    )
        THROW 51118,
            'Smoke test failed: rubric/rubric criterion relationship is invalid.',
            1;


    /* Evaluation must use the rubric and one of that rubric's criteria. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.evaluations e
        JOIN dbo.evaluation_details ed ON ed.evaluation_id = e.id
        JOIN dbo.rubric_criteria rc ON rc.id = ed.rubric_criterion_id
        WHERE e.id = @evaluation_id
          AND e.project_id = @project_id
          AND e.rubric_id = @rubric_id
          AND ed.id = @evaluation_detail_id
          AND ed.rubric_criterion_id = @rubric_criterion_id
          AND rc.rubric_id = @rubric_id
          AND ed.score = 9.00
          AND e.evidence_summary = N'Smoke-test evidence summary'
          AND e.finalized_by = @lecturer_id
          AND e.finalized_at IS NOT NULL
          AND e.rounding_rule = N'AWAY_FROM_ZERO_2DP'
    )
        THROW 51119,
            'Smoke test failed: rubric-based evaluation relationship is invalid.',
            1;


    /* Notification was sent to both students. */
    IF (
        SELECT COUNT(*)
        FROM dbo.notification_recipients
        WHERE notification_id = @notification_id
          AND user_id IN (@student1_id, @student2_id)
    ) <> 2
        THROW 51120,
            'Smoke test failed: notification recipient relationships are invalid.',
            1;


    /* Final high-level sanity join across major project entities. */
    IF NOT EXISTS (
        SELECT 1
        FROM dbo.projects p
        JOIN dbo.teams t
            ON t.id = p.team_id
        JOIN dbo.supervisor_assignments sa
            ON sa.project_id = p.id
        JOIN dbo.milestones m
            ON m.project_id = p.id
        JOIN dbo.tasks tk
            ON tk.milestone_id = m.id
        JOIN dbo.progress_reports pr
            ON pr.project_id = p.id
        JOIN dbo.deliverables d
            ON d.project_id = p.id
        JOIN dbo.evaluations e
            ON e.project_id = p.id
        JOIN dbo.rubrics r
            ON r.id = e.rubric_id
        JOIN dbo.evaluation_details ed
            ON ed.evaluation_id = e.id
        JOIN dbo.rubric_criteria rc
            ON rc.id = ed.rubric_criterion_id
           AND rc.rubric_id = r.id
        WHERE p.id = @project_id
          AND t.id = @team_id
          AND sa.id = @assignment_id
          AND r.id = @rubric_id
    )
        THROW 51121,
            'Smoke test failed: end-to-end project relationship chain is invalid.',
            1;

    /* =========================================================
       16. SUCCESS + ROLLBACK
       ========================================================= */

    PRINT N'AI-PMS end-to-end smoke test passed.';
    PRINT N'All smoke-test data will now be rolled back.';

    ROLLBACK TRANSACTION;
END TRY

BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'AI-PMS smoke test failed.';
    PRINT ERROR_MESSAGE();

    THROW;
END CATCH;
GO

/* =========================================================================
   17. NEGATIVE TESTS & CONSTRAINT VERIFICATIONS
   ========================================================================= */

-- Ensure XACT_ABORT is OFF for negative testing so we can catch exceptions without batch abort
SET XACT_ABORT OFF;
GO

-- A. Test Duplicate Tag & Unique Constraint on Tags
BEGIN TRANSACTION;
BEGIN TRY
    -- 1. Setup minimal data
    DECLARE @org_id BIGINT, @dept_id BIGINT, @major_id BIGINT, @semester_id BIGINT, @period_id BIGINT, @user_id BIGINT, @team_id BIGINT, @project_id BIGINT;

    INSERT INTO dbo.organizations (code, name, is_active) VALUES (N'ORG-N1', N'Org Neg 1', 1);
    SET @org_id = SCOPE_IDENTITY();

    INSERT INTO dbo.departments (organization_id, code, name, is_active) VALUES (@org_id, N'IT-N1', N'IT Neg 1', 1);
    SET @dept_id = SCOPE_IDENTITY();

    INSERT INTO dbo.majors (department_id, code, name, is_active) VALUES (@dept_id, N'SE-N1', N'SE Neg 1', 1);
    SET @major_id = SCOPE_IDENTITY();

    INSERT INTO dbo.academic_semesters (organization_id, code, name, start_date, end_date, status)
    VALUES (@org_id, N'FA26-N1', N'Fall 2026 Neg 1', '2026-09-01', '2026-12-31', N'ACTIVE');
    SET @semester_id = SCOPE_IDENTITY();

    INSERT INTO dbo.project_periods (academic_semester_id, code, name, period_type, start_at, end_at, status)
    VALUES (@semester_id, N'REG-N1', N'Reg Neg 1', N'REGISTRATION', '2026-08-15', '2026-08-31', N'ACTIVE');
    SET @period_id = SCOPE_IDENTITY();

    INSERT INTO dbo.users (department_id, email, password_hash, full_name, status)
    VALUES (@dept_id, N'student.neg1@aipms.test', N'HASH', N'Student Neg 1', N'ACTIVE');
    SET @user_id = SCOPE_IDENTITY();

    INSERT INTO dbo.teams (academic_semester_id, code, name, status, created_by)
    VALUES (@semester_id, N'TEAM-N1', N'Team Neg 1', N'FORMING', @user_id);
    SET @team_id = SCOPE_IDENTITY();

    INSERT INTO dbo.projects (team_id, code, title, status, created_by)
    VALUES (@team_id, N'PROJ-N1', N'Proj Neg 1', N'DRAFT', @user_id);
    SET @project_id = SCOPE_IDENTITY();

    -- 2. Test duplicate master tag (uq_tags_normalized_name_type constraint)
    DECLARE @tag_id1 BIGINT;
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Unique Tag 1', N'UNIQUE_TAG_1', N'KEYWORD');
    SET @tag_id1 = SCOPE_IDENTITY();

    BEGIN TRY
        INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Unique Tag 1 Dup', N'UNIQUE_TAG_1', N'KEYWORD');
        THROW 51092, 'Negative test failed: duplicate master tag was not rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2627, 2601)
        BEGIN
            DECLARE @Err NVARCHAR(MAX) = N'Unexpected error during duplicate tag test: ' + ERROR_MESSAGE();
            THROW 51093, @Err, 1;
        END
        PRINT 'Duplicate master tag constraint correctly rejected.';
    END CATCH

    -- 3. Test duplicate tag association (pk_project_tags constraint)
    INSERT INTO dbo.project_tags (project_id, tag_id) VALUES (@project_id, @tag_id1);

    BEGIN TRY
        INSERT INTO dbo.project_tags (project_id, tag_id) VALUES (@project_id, @tag_id1);
        THROW 51094, 'Negative test failed: duplicate tag association was not rejected.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2627, 2601)
        BEGIN
            DECLARE @Err2 NVARCHAR(MAX) = N'Unexpected error during duplicate association test: ' + ERROR_MESSAGE();
            THROW 51095, @Err2, 1;
        END
        PRINT 'Duplicate tag association correctly rejected.';
    END CATCH

    -- 4. Test duplicate active project for same team (uq_projects_active_team index)
    BEGIN TRY
        -- This second project is in DRAFT (active status) for the same team -> must fail
        INSERT INTO dbo.projects (team_id, code, title, status, created_by)
        VALUES (@team_id, N'PROJ-N2', N'Proj Neg 2', N'DRAFT', @user_id);

        THROW 51096, 'Negative test failed: second active project for the same team was not rejected.', 1;
    END TRY
    BEGIN CATCH
        -- Unique index violation is also error 2601 or 2627
        IF ERROR_NUMBER() NOT IN (2627, 2601)
        BEGIN
            DECLARE @Err3 NVARCHAR(MAX) = N'Unexpected error during active project index test: ' + ERROR_MESSAGE();
            THROW 51097, @Err3, 1;
        END
        PRINT 'Second active project for same team correctly rejected.';
    END CATCH

    -- 5. Test team can register a new project after first is REJECTED
    -- Transition first project to REJECTED (finished status)
    UPDATE dbo.projects SET status = N'REJECTED' WHERE id = @project_id;

    -- Registering new project in DRAFT status should now succeed!
    DECLARE @new_proj_id BIGINT;
    INSERT INTO dbo.projects (team_id, code, title, status, created_by)
    VALUES (@team_id, N'PROJ-N2', N'Proj Neg 2', N'DRAFT', @user_id);
    SET @new_proj_id = SCOPE_IDENTITY();

    IF @new_proj_id IS NULL
    BEGIN
        THROW 51098, 'Negative test failed: failed to insert new active project after first was rejected.', 1;
    END
    PRINT 'Registration of a new project after first is REJECTED succeeded.';

    ROLLBACK TRANSACTION;
    PRINT 'All negative tests passed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
