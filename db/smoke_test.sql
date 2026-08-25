/*
 AI-PMS transactional smoke test.
 Run AFTER schema.sql and seed.sql.

 It exercises one end-to-end non-AI project flow and rolls everything back.

 Coverage:
 - Academic structure
 - Roles / users
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
        role_id
    )
    VALUES
        (@student1_id, @student_role_id),
        (@student2_id, @student_role_id),
        (@lecturer_id, @lecturer_role_id);

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


    INSERT INTO dbo.project_keywords(
        project_id,
        keyword
    )
    VALUES
        (@project_id, N'AI'),
        (@project_id, N'Smoke');


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
        is_active,
        created_by
    )
    VALUES (
        @dept_id,
        @semester_id,
        N'__SMOKE_RUBRIC__',
        N'AI-PMS Smoke Test Rubric',
        N'Rubric used only inside the transactional smoke test.',
        1,
        @lecturer_id
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
        evaluated_at
    )
    VALUES (
        @project_id,
        @lecturer_id,
        @rubric_id,
        N'SUPERVISOR',
        N'FINALIZED',
        9.00,
        N'Smoke evaluation',
        SYSUTCDATETIME()
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
