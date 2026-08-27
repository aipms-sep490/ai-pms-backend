/*
 AI-PMS reference data for local development and integration testing
 Target: Microsoft SQL Server

 Purpose:
 - Populate all 37 tables in the current AI-PMS schema with coherent academic data.
 - Align the scenario with FPT University, the FA26 semester, and the SEP490 Capstone Project context.
 - Cover multi-major teams/projects, supervisor workflow, milestones/tasks,
   reports, meetings, deliverables/files, evaluation, and notifications.

 Prerequisites:
   1) schema.sql
   2) seed.sql
   3) sample_data.sql   <-- this file

 Notes:
 - The academic context follows the AI-PMS capstone requirements and FPTU-style program naming.
 - Personal names, account identifiers, and contact fields are reference records for development use.
 - Development accounts use ASP.NET Core Identity V3-compatible PBKDF2 hashes for the common local password Aipms@123. Do not use this password outside local development/testing.
 - Re-running this script is safe: if organization FPTU_DN already exists,
   the script exits without inserting duplicates.
*/

USE [AI_PMS];
GO
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF EXISTS (SELECT 1 FROM dbo.organizations WHERE code = N'FPTU_DN')
BEGIN
    PRINT N'AI-PMS reference data already exists (organization code FPTU_DN). No changes were made.';
    RETURN;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.roles WHERE code = N'ADMIN')
   OR NOT EXISTS (SELECT 1 FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF')
   OR NOT EXISTS (SELECT 1 FROM dbo.roles WHERE code = N'LECTURER')
   OR NOT EXISTS (SELECT 1 FROM dbo.roles WHERE code = N'STUDENT')
BEGIN
    THROW 51000, 'Required roles are missing. Run seed.sql before sample_data.sql.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    /* =========================================================
       1. ACADEMIC STRUCTURE
       ========================================================= */

    INSERT INTO dbo.organizations (code, name, description, is_active)
    VALUES (
        N'FPTU_DN',
        N'FPT University - Da Nang Campus',
        N'Academic organization for the AI-PMS SEP490 Capstone Project scenario at FPT University - Da Nang Campus.',
        1
    );

    DECLARE @org_id BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.departments (organization_id, code, name, description, is_active)
    VALUES
        (@org_id, N'IT', N'Information Technology', N'Academic unit for Information Technology programs, including Software Engineering, Artificial Intelligence, Applied Data Science, and Information Assurance.', 1),
        (@org_id, N'BA', N'Business Administration', N'Academic unit for business and management programs that may participate in multidisciplinary university projects.', 1);

    DECLARE @it_dept_id BIGINT = (SELECT id FROM dbo.departments WHERE organization_id = @org_id AND code = N'IT');
    DECLARE @ba_dept_id BIGINT = (SELECT id FROM dbo.departments WHERE organization_id = @org_id AND code = N'BA');

    INSERT INTO dbo.majors (department_id, code, name, description, is_active)
    VALUES
        (@it_dept_id, N'SE',  N'Software Engineering', N'Software architecture, backend/frontend development, testing, DevOps, and software project management.', 1),
        (@it_dept_id, N'AI',  N'Artificial Intelligence', N'Machine learning, natural language processing, recommendation systems, computer vision, and intelligent applications.', 1),
        (@it_dept_id, N'ADS', N'Applied Data Science', N'Data analytics, statistics, machine learning, visualization, big-data processing, and applied predictive modeling.', 1),
        (@it_dept_id, N'IA',  N'Information Assurance', N'Information security, secure systems, risk management, networks, and information assurance.', 1),
        (@ba_dept_id, N'MKT', N'Marketing', N'Marketing strategy, customer insights, digital channels, communication, and data-informed business decision making.', 1);

    DECLARE @major_se BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @it_dept_id AND code = N'SE');
    DECLARE @major_ai BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @it_dept_id AND code = N'AI');
    DECLARE @major_ads BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @it_dept_id AND code = N'ADS');
    DECLARE @major_ia BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @it_dept_id AND code = N'IA');
    DECLARE @major_mkt BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @ba_dept_id AND code = N'MKT');

    INSERT INTO dbo.academic_semesters (organization_id, code, name, start_date, end_date, status)
    VALUES
        (@org_id, N'FA26', N'Fall 2026',  '2026-09-01', '2026-12-31', N'ACTIVE'),
        (@org_id, N'SP27', N'Spring 2027','2027-01-05', '2027-04-30', N'UPCOMING');

    DECLARE @semester_fa26 BIGINT = (SELECT id FROM dbo.academic_semesters WHERE organization_id = @org_id AND code = N'FA26');
    DECLARE @semester_sp27 BIGINT = (SELECT id FROM dbo.academic_semesters WHERE organization_id = @org_id AND code = N'SP27');

    INSERT INTO dbo.project_periods
        (academic_semester_id, code, name, period_type, start_at, end_at, status)
    VALUES
        (@semester_fa26, N'SEP490-FA26-REG',    N'SEP490 Project Registration', N'REGISTRATION',     '2026-08-15T08:00:00', '2026-08-31T23:59:59', N'CLOSED'),
        (@semester_fa26, N'SEP490-FA26-REVIEW', N'SEP490 Proposal Review',      N'PROJECT_REVIEW',  '2026-09-01T08:00:00', '2026-09-15T23:59:59', N'CLOSED'),
        (@semester_fa26, N'SEP490-FA26-EXEC',   N'SEP490 Project Execution',    N'EXECUTION',        '2026-09-16T00:00:00', '2026-12-10T23:59:59', N'ACTIVE'),
        (@semester_fa26, N'SEP490-FA26-FINAL',  N'SEP490 Final Submission and Defense', N'FINAL_SUBMISSION', '2026-12-11T00:00:00', '2026-12-31T23:59:59', N'UPCOMING'),
        (@semester_sp27, N'SEP490-SP27-REG',    N'SEP490 Project Registration', N'REGISTRATION',     '2026-12-15T08:00:00', '2027-01-04T23:59:59', N'UPCOMING');

    /* =========================================================
       2. IDENTITY / RBAC
       ========================================================= */

    /* Local development password for the non-admin reference accounts: Aipms@123 */

    INSERT INTO dbo.users
        (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status)
    VALUES
        (@it_dept_id,   NULL,       N'sep490.coordinator@fpt.edu.vn', N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==', N'Lê Thu Hà',          NULL, NULL,        N'FPTU-AA-001',  N'Academic Coordinator', N'ACTIVE'),
        (@it_dept_id,   NULL,       N'lecturer.se01@fe.edu.vn',       N'AQAAAAIAAYagAAAAEOBbobgnKQMjqP9sD8+2dDfHZqaMiuFBZu6n9wE19ac9rK3AoLVe1kqUT1wKqsLivw==', N'Nguyễn Hoàng Minh', NULL, NULL,        N'FPTU-SE-001',  N'Lecturer', N'ACTIVE'),
        (@it_dept_id,   NULL,       N'lecturer.ai01@fe.edu.vn',       N'AQAAAAIAAYagAAAAEG2JyqVBEGVDicbpLjzhrrW6QDIH2NrD9IOUvK4mLyarWxeSAkTv3hhT5uZ7sBOX/w==', N'Trần Thu Trang',     NULL, NULL,        N'FPTU-AI-001',  N'Lecturer', N'ACTIVE'),
        (@it_dept_id,   NULL,       N'lecturer.ads01@fe.edu.vn',      N'AQAAAAIAAYagAAAAEDzhMc3vmhEsNY87qwkS7Rahi/ijxWFfHJv2uS2EY+9zQGlCYa1fh0blemCqRCydEA==', N'Phạm Đức Long',      NULL, NULL,        N'FPTU-ADS-001', N'Lecturer', N'ACTIVE'),
        (NULL,          @major_se,  N'khangnmde180901@fpt.edu.vn',    N'AQAAAAIAAYagAAAAEBQwoh0opIcLGaqiNKAbOrokW23aBWK6MBO3UdF/xa9dTuJkzKHaOOAxGAvi4PzjMw==', N'Nguyễn Minh Khang', NULL, N'DE180901', NULL, N'Student', N'ACTIVE'),
        (NULL,          @major_se,  N'hantgde180902@fpt.edu.vn',      N'AQAAAAIAAYagAAAAEO1JBFhfygLcTAJ3WQ7w8zjDYdEh2ijm/2yjWbFP40pLvMnOK2xG2S4i8MQkxBjhyg==', N'Trần Gia Hân',       NULL, N'DE180902', NULL, N'Student', N'ACTIVE'),
        (NULL,          @major_ai,  N'baopqde180903@fpt.edu.vn',      N'AQAAAAIAAYagAAAAEAs1KO2WlRZONEzZocgdrfN3HLkFjMK8Ghtodef0/bhtLXRgv4IGgLVoFygMGlib6w==', N'Phạm Quốc Bảo',      NULL, N'DE180903', NULL, N'Student', N'ACTIVE'),
        (NULL,          @major_mkt, N'linhlkde180904@fpt.edu.vn',     N'AQAAAAIAAYagAAAAED1VaHfGqkSUp8mtQ9pXA1L9prNp2dqDESMqQKcuCF9Qe1uqyogfb+HLHQObmDbcEA==', N'Lê Khánh Linh',      NULL, N'DE180904', NULL, N'Student', N'ACTIVE'),
        (NULL,          @major_ads, N'duyvade180905@fpt.edu.vn',      N'AQAAAAIAAYagAAAAENca+tf9wkmrgohuX+5JTctOWJT5EYgnPe0KAxzv6mOaRnqamOjT5C92jM/sVEDQGw==', N'Võ Anh Duy',         NULL, N'DE180905', NULL, N'Student', N'ACTIVE'),
        (NULL,          @major_ia,  N'maintde180906@fpt.edu.vn',      N'AQAAAAIAAYagAAAAENY/4G+YA0IR+390JrbN4k+mNPCIZTejBYfSYIpc+eztqDqD0R11Jeafwf6h+NQi8A==', N'Ngô Thanh Mai',      NULL, N'DE180906', NULL, N'Student', N'ACTIVE');

    DECLARE @staff_id BIGINT     = (SELECT id FROM dbo.users WHERE email = N'sep490.coordinator@fpt.edu.vn');
    DECLARE @lecturer1_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.se01@fe.edu.vn');
    DECLARE @lecturer2_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.ai01@fe.edu.vn');
    DECLARE @lecturer3_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.ads01@fe.edu.vn');
    DECLARE @student1_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'khangnmde180901@fpt.edu.vn');
    DECLARE @student2_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'hantgde180902@fpt.edu.vn');
    DECLARE @student3_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'baopqde180903@fpt.edu.vn');
    DECLARE @student4_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'linhlkde180904@fpt.edu.vn');
    DECLARE @student5_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'duyvade180905@fpt.edu.vn');
    DECLARE @student6_id BIGINT  = (SELECT id FROM dbo.users WHERE email = N'maintde180906@fpt.edu.vn');

    DECLARE @role_staff BIGINT   = (SELECT id FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF');
    DECLARE @role_lecturer BIGINT= (SELECT id FROM dbo.roles WHERE code = N'LECTURER');
    DECLARE @role_student BIGINT = (SELECT id FROM dbo.roles WHERE code = N'STUDENT');

    INSERT INTO dbo.user_roles (user_id, role_id)
    VALUES
        (@staff_id, @role_staff),
        (@lecturer1_id, @role_lecturer),
        (@lecturer2_id, @role_lecturer),
        (@lecturer3_id, @role_lecturer),
        (@student1_id, @role_student),
        (@student2_id, @role_student),
        (@student3_id, @role_student),
        (@student4_id, @role_student),
        (@student5_id, @role_student),
        (@student6_id, @role_student);

    /* =========================================================
       3. TEAM MANAGEMENT
       ========================================================= */

    INSERT INTO dbo.teams
        (academic_semester_id, code, name, description, status, created_by)
    VALUES
        (@semester_fa26, N'SEP490_G01', N'AI-PMS',
         N'SEP490 capstone team developing AI-PMS with members from Information Technology and Business Administration specializations to exercise multi-major and cross-department collaboration.',
         N'LOCKED', @student1_id);

    DECLARE @team_id BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.team_members
        (team_id, academic_semester_id, user_id, is_leader, joined_at)
    VALUES
        (@team_id, @semester_fa26, @student1_id, 1, '2026-08-18T09:00:00'),
        (@team_id, @semester_fa26, @student2_id, 0, '2026-08-18T09:10:00'),
        (@team_id, @semester_fa26, @student3_id, 0, '2026-08-18T09:15:00'),
        (@team_id, @semester_fa26, @student4_id, 0, '2026-08-19T10:00:00'),
        (@team_id, @semester_fa26, @student5_id, 0, '2026-08-20T08:30:00');

    INSERT INTO dbo.team_invitations
        (team_id, invited_user_id, invited_by, status, message, expires_at, responded_at)
    VALUES
        (@team_id, @student5_id, @student1_id, N'ACCEPTED',
         N'Join the AI-PMS team as the Applied Data Science member responsible for analytics data preparation.',
         '2026-08-22T23:59:59', '2026-08-20T08:25:00'),
        (@team_id, @student6_id, @student1_id, N'CANCELLED',
         N'Invitation was withdrawn after the five-member team composition was finalized for project execution.',
         '2026-09-01T23:59:59', '2026-08-28T17:00:00');

    /* =========================================================
       4. PROJECT MANAGEMENT
       ========================================================= */

    INSERT INTO dbo.projects
        (team_id, code, title, description, objectives, status,
         registered_at, submitted_at, approved_at, completed_at, created_by,
         problem_statement, expected_output)
    VALUES
        (@team_id,
         N'SEP490-FA26-AIPMS',
         N'Building an AI-Powered Multi-disciplinary and Multi-major Academic Project Progress Management System for Universities',
         N'Web-based academic project management platform supporting multi-major team formation, project approval, supervisor assignment, milestones, tasks, reports, deliverables, meetings, feedback, evaluation, notifications, and future AI services.',
         N'Provide transparent academic project progress tracking; support multi-major and cross-department collaboration; improve supervision and feedback; record deliverables and evaluations; prepare a clean data foundation for later AI progress analysis, risk prediction, supervisor recommendation, report summarization, and chatbot features.',
         N'ACTIVE',
         '2026-08-20T10:00:00',
         '2026-08-28T14:00:00',
         '2026-09-10T15:30:00',
         NULL,
         @student1_id,
         N'Universities lack a unified platform to manage complex multi-disciplinary academic projects, leading to disjointed communication, tracking difficulties, and lack of historical data for AI evaluation.',
         N'A fully operational web application with functional dashboard, timeline, meeting tracker, automatic notifications, and an AI advisory chatbot.');

    DECLARE @project_id BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.project_majors (project_id, major_id)
    VALUES
        (@project_id, @major_se),
        (@project_id, @major_ai),
        (@project_id, @major_ads),
        (@project_id, @major_mkt);

    -- Insert tags for Domain, Technology, Keywords
    DECLARE @tag_dom1 BIGINT, @tag_dom2 BIGINT;
    DECLARE @tag_tech1 BIGINT, @tag_tech2 BIGINT, @tag_tech3 BIGINT, @tag_tech4 BIGINT, @tag_tech5 BIGINT, @tag_tech6 BIGINT;
    DECLARE @tag_kw1 BIGINT, @tag_kw2 BIGINT, @tag_kw3 BIGINT, @tag_kw4 BIGINT;

    -- Domains
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Education Technology', N'EDUCATION_TECHNOLOGY', N'DOMAIN');
    SET @tag_dom1 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Artificial Intelligence', N'ARTIFICIAL_INTELLIGENCE', N'DOMAIN');
    SET @tag_dom2 = SCOPE_IDENTITY();

    -- Technologies
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'.NET 8', N'NET_8', N'TECHNOLOGY');
    SET @tag_tech1 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'React', N'REACT', N'TECHNOLOGY');
    SET @tag_tech2 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'SQL Server', N'SQL_SERVER', N'TECHNOLOGY');
    SET @tag_tech3 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Redis', N'REDIS', N'TECHNOLOGY');
    SET @tag_tech4 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Python', N'PYTHON', N'TECHNOLOGY');
    SET @tag_tech5 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'Docker', N'DOCKER', N'TECHNOLOGY');
    SET @tag_tech6 = SCOPE_IDENTITY();

    -- Keywords
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'AI_PMS', N'AI_PMS', N'KEYWORD');
    SET @tag_kw1 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'EdTech', N'EDTECH', N'KEYWORD');
    SET @tag_kw2 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'MultiMajor', N'MULTIMAJOR', N'KEYWORD');
    SET @tag_kw3 = SCOPE_IDENTITY();
    INSERT INTO dbo.tags (name, normalized_name, tag_type) VALUES (N'AcademicManagement', N'ACADEMICMANAGEMENT', N'KEYWORD');
    SET @tag_kw4 = SCOPE_IDENTITY();

    INSERT INTO dbo.project_tags (project_id, tag_id)
    VALUES
        (@project_id, @tag_dom1),
        (@project_id, @tag_dom2),
        (@project_id, @tag_tech1),
        (@project_id, @tag_tech2),
        (@project_id, @tag_tech3),
        (@project_id, @tag_tech4),
        (@project_id, @tag_tech5),
        (@project_id, @tag_tech6),
        (@project_id, @tag_kw1),
        (@project_id, @tag_kw2),
        (@project_id, @tag_kw3),
        (@project_id, @tag_kw4);

    INSERT INTO dbo.project_status_history
        (project_id, old_status, new_status, changed_by, reason, changed_at)
    VALUES
        (@project_id, NULL, N'DRAFT', @student1_id, N'Initial project registration created by team leader.', '2026-08-20T10:00:00'),
        (@project_id, N'DRAFT', N'SUBMITTED', @student1_id, N'Team completed the initial proposal and submitted it for department review.', '2026-08-28T14:00:00'),
        (@project_id, N'SUBMITTED', N'UNDER_REVIEW', @staff_id, N'Department staff started proposal review.', '2026-09-02T09:00:00'),
        (@project_id, N'UNDER_REVIEW', N'APPROVED', @staff_id, N'Proposal satisfies multi-major capstone requirements and was approved.', '2026-09-10T15:30:00'),
        (@project_id, N'APPROVED', N'SUPERVISOR_PENDING', @staff_id, N'Project moved to supervisor matching and assignment stage.', '2026-09-10T15:35:00'),
        (@project_id, N'SUPERVISOR_PENDING', N'ACTIVE', @staff_id, N'Primary and co-supervisor assignments were confirmed.', '2026-09-12T16:00:00');

    /* =========================================================
       5. SUPERVISOR MANAGEMENT
       ========================================================= */

    INSERT INTO dbo.supervisor_profiles (user_id, bio, max_active_projects, is_available)
    VALUES
        (@lecturer1_id, N'Software engineering lecturer specializing in software architecture, ASP.NET Core Web API, REST APIs, database design, and capstone supervision.', 3, 1),
        (@lecturer2_id, N'AI lecturer focusing on NLP, recommendation systems, learning analytics, and applied AI for education.', 2, 1),
        (@lecturer3_id, N'Lecturer specializing in cloud deployment, DevOps, Docker, CI/CD, and scalable backend infrastructure.', 3, 1);

    DECLARE @sup1 BIGINT = (SELECT id FROM dbo.supervisor_profiles WHERE user_id = @lecturer1_id);
    DECLARE @sup2 BIGINT = (SELECT id FROM dbo.supervisor_profiles WHERE user_id = @lecturer2_id);
    DECLARE @sup3 BIGINT = (SELECT id FROM dbo.supervisor_profiles WHERE user_id = @lecturer3_id);

    INSERT INTO dbo.supervisor_expertise (supervisor_profile_id, expertise_name, proficiency_level)
    VALUES
        (@sup1, N'Software Architecture', N'EXPERT'),
        (@sup1, N'ASP.NET Core', N'EXPERT'),
        (@sup1, N'SQL Server Database Design', N'ADVANCED'),
        (@sup1, N'Academic Project Supervision', N'EXPERT'),
        (@sup2, N'Natural Language Processing', N'EXPERT'),
        (@sup2, N'Recommendation Systems', N'ADVANCED'),
        (@sup2, N'Learning Analytics', N'ADVANCED'),
        (@sup2, N'Large Language Models', N'ADVANCED'),
        (@sup3, N'Docker and CI/CD', N'EXPERT'),
        (@sup3, N'Cloud Deployment', N'ADVANCED');

    INSERT INTO dbo.supervisor_requests
        (project_id, supervisor_profile_id, requested_by, status, request_message, response_message, requested_at, responded_at)
    VALUES
        (@project_id, @sup1, @student1_id, N'ACCEPTED',
         N'We request supervision for the software architecture, backend, SQL Server schema, and project management workflow of AI-PMS.',
         N'Accepted. I will serve as primary supervisor and focus on system architecture, backend design, database quality, and execution planning.',
         '2026-09-10T16:00:00', '2026-09-11T09:30:00'),
        (@project_id, @sup2, @student1_id, N'ACCEPTED',
         N'We request co-supervision for future AI progress analysis, NLP report summarization, and supervisor recommendation components.',
         N'Accepted as co-supervisor. AI tables can be introduced in a later phase after the core data model stabilizes.',
         '2026-09-10T16:15:00', '2026-09-11T13:00:00'),
        (@project_id, @sup3, @student1_id, N'REJECTED',
         N'We would also like support for deployment and DevOps if supervision capacity is available.',
         N'Unable to join as an official supervisor this semester due to supervision capacity. I can provide informal deployment guidance later.',
         '2026-09-10T16:30:00', '2026-09-12T08:00:00');

    DECLARE @req1 BIGINT = (SELECT id FROM dbo.supervisor_requests WHERE project_id = @project_id AND supervisor_profile_id = @sup1);
    DECLARE @req2 BIGINT = (SELECT id FROM dbo.supervisor_requests WHERE project_id = @project_id AND supervisor_profile_id = @sup2);

    INSERT INTO dbo.supervisor_assignments
        (project_id, supervisor_profile_id, supervisor_request_id, is_primary, assigned_at)
    VALUES
        (@project_id, @sup1, @req1, 1, '2026-09-12T09:00:00'),
        (@project_id, @sup2, @req2, 0, '2026-09-12T09:05:00');

    DECLARE @assignment1 BIGINT = (SELECT id FROM dbo.supervisor_assignments WHERE supervisor_request_id = @req1);
    DECLARE @assignment2 BIGINT = (SELECT id FROM dbo.supervisor_assignments WHERE supervisor_request_id = @req2);

    /* =========================================================
       6. MILESTONES AND TASKS
       ========================================================= */

    INSERT INTO dbo.milestones
        (project_id, title, description, start_date, due_date, status, sort_order, created_by)
    VALUES
        (@project_id, N'M1 - Requirements and Proposal', N'Analyze university academic-project workflows, finalize scope, proposal, user roles, and core database requirements.', '2026-09-01', '2026-09-15', N'COMPLETED', 1, @student1_id),
        (@project_id, N'M2 - Architecture and Core MVP', N'Design architecture and ERD; implement authentication, RBAC, team/project APIs, and initial dashboard.', '2026-09-16', '2026-10-15', N'COMPLETED', 2, @student1_id),
        (@project_id, N'M3 - Execution and Collaboration Modules', N'Implement milestones, tasks, progress reports, meetings, deliverables, files, feedback, evaluation, and notifications.', '2026-10-16', '2026-11-30', N'IN_PROGRESS', 3, @student1_id),
        (@project_id, N'M4 - Final Submission and Defense', N'Finalize testing, technical documentation, final report, deployment evidence, and project defense materials.', '2026-12-01', '2026-12-31', N'PLANNED', 4, @student1_id);

    DECLARE @m1 BIGINT = (SELECT id FROM dbo.milestones WHERE project_id = @project_id AND sort_order = 1);
    DECLARE @m2 BIGINT = (SELECT id FROM dbo.milestones WHERE project_id = @project_id AND sort_order = 2);
    DECLARE @m3 BIGINT = (SELECT id FROM dbo.milestones WHERE project_id = @project_id AND sort_order = 3);
    DECLARE @m4 BIGINT = (SELECT id FROM dbo.milestones WHERE project_id = @project_id AND sort_order = 4);

    INSERT INTO dbo.tasks
        (milestone_id, parent_task_id, title, description, status, priority, start_at, due_at, completed_at, created_by)
    VALUES
        (@m1, NULL, N'Analyze academic project workflow', N'Document registration, approval, team, supervision, execution, submission, and evaluation flows from the project requirements.', N'DONE', N'HIGH', '2026-09-01T08:00:00', '2026-09-05T18:00:00', '2026-09-05T16:00:00', @student1_id),
        (@m1, NULL, N'Design initial SQL Server ERD', N'Design the normalized SQL Server data model for all non-AI core modules and validate PK/FK/cardinality rules.', N'DONE', N'CRITICAL', '2026-09-04T09:00:00', '2026-09-10T18:00:00', '2026-09-10T17:00:00', @student1_id),
        (@m1, NULL, N'Prepare approved project proposal', N'Consolidate scope, objectives, technical approach, timeline, and supervisor-request material.', N'DONE', N'MEDIUM', '2026-09-06T08:00:00', '2026-09-14T18:00:00', '2026-09-14T15:00:00', @student1_id),
        (@m2, NULL, N'Implement authentication and RBAC', N'Implement ASP.NET Core authentication with JWT bearer tokens and role-based authorization for ADMIN, DEPARTMENT_STAFF, LECTURER, and STUDENT.', N'DONE', N'HIGH', '2026-09-16T08:00:00', '2026-09-25T18:00:00', '2026-09-24T17:30:00', @student1_id),
        (@m2, NULL, N'Implement team and project APIs', N'Implement team formation, invitations, leader handling, multi-major project registration, and project status APIs.', N'DONE', N'HIGH', '2026-09-24T08:00:00', '2026-10-05T18:00:00', '2026-10-05T17:00:00', @student1_id),
        (@m2, NULL, N'Build student project dashboard', N'Create project overview, team status, milestone summary, recent tasks, and notification widgets.', N'DONE', N'MEDIUM', '2026-10-01T08:00:00', '2026-10-15T18:00:00', '2026-10-14T16:30:00', @student1_id),
        (@m3, NULL, N'Implement milestone and task module', N'Implement milestone CRUD, task status, multi-assignee support, dependencies, and status history.', N'IN_PROGRESS', N'HIGH', '2026-10-16T08:00:00', '2026-10-31T18:00:00', NULL, @student1_id),
        (@m3, NULL, N'Implement progress reports and deliverables', N'Implement weekly/monthly reports, deliverables, versioning, file metadata, and supervisor feedback workflows.', N'TODO', N'HIGH', '2026-11-01T08:00:00', '2026-11-15T18:00:00', NULL, @student1_id),
        (@m3, NULL, N'Integrate file metadata repository', N'Connect object-storage paths/URLs with file metadata records; actual uploaded file bytes remain outside SQL Server.', N'BLOCKED', N'MEDIUM', '2026-11-08T08:00:00', '2026-11-20T18:00:00', NULL, @student1_id),
        (@m4, NULL, N'Prepare final technical report', N'Finalize architecture, database design, API documentation, testing evidence, evaluation results, and limitations.', N'TODO', N'HIGH', '2026-12-01T08:00:00', '2026-12-20T18:00:00', NULL, @student1_id),
        (@m4, NULL, N'Prepare defense presentation', N'Prepare final project presentation, presentation slides, data-flow explanation, and team contribution summary.', N'TODO', N'MEDIUM', '2026-12-15T08:00:00', '2026-12-28T18:00:00', NULL, @student1_id);

    DECLARE @t_workflow BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m1 AND title = N'Analyze academic project workflow');
    DECLARE @t_erd BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m1 AND title = N'Design initial SQL Server ERD');
    DECLARE @t_proposal BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m1 AND title = N'Prepare approved project proposal');
    DECLARE @t_auth BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m2 AND title = N'Implement authentication and RBAC');
    DECLARE @t_project_api BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m2 AND title = N'Implement team and project APIs');
    DECLARE @t_dashboard BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m2 AND title = N'Build student project dashboard');
    DECLARE @t_task_module BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m3 AND title = N'Implement milestone and task module');
    DECLARE @t_reports BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m3 AND title = N'Implement progress reports and deliverables');
    DECLARE @t_files BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m3 AND title = N'Integrate file metadata repository');
    DECLARE @t_final_report BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m4 AND title = N'Prepare final technical report');
    DECLARE @t_defense BIGINT = (SELECT id FROM dbo.tasks WHERE milestone_id = @m4 AND title = N'Prepare defense presentation');

    INSERT INTO dbo.task_assignees (task_id, user_id, assigned_by, assigned_at)
    VALUES
        (@t_workflow, @student1_id, @student1_id, '2026-09-01T08:00:00'),
        (@t_workflow, @student4_id, @student1_id, '2026-09-01T08:05:00'),
        (@t_erd, @student1_id, @student1_id, '2026-09-04T09:00:00'),
        (@t_erd, @student2_id, @student1_id, '2026-09-04T09:05:00'),
        (@t_proposal, @student1_id, @student1_id, '2026-09-06T08:00:00'),
        (@t_proposal, @student4_id, @student1_id, '2026-09-06T08:05:00'),
        (@t_auth, @student2_id, @student1_id, '2026-09-16T08:00:00'),
        (@t_auth, @student1_id, @student1_id, '2026-09-16T08:05:00'),
        (@t_project_api, @student1_id, @student1_id, '2026-09-24T08:00:00'),
        (@t_project_api, @student2_id, @student1_id, '2026-09-24T08:05:00'),
        (@t_dashboard, @student4_id, @student1_id, '2026-10-01T08:00:00'),
        (@t_dashboard, @student5_id, @student1_id, '2026-10-01T08:05:00'),
        (@t_task_module, @student2_id, @student1_id, '2026-10-16T08:00:00'),
        (@t_task_module, @student3_id, @student1_id, '2026-10-16T08:05:00'),
        (@t_reports, @student3_id, @student1_id, '2026-11-01T08:00:00'),
        (@t_reports, @student4_id, @student1_id, '2026-11-01T08:05:00'),
        (@t_files, @student5_id, @student1_id, '2026-11-08T08:00:00'),
        (@t_final_report, @student1_id, @student1_id, '2026-12-01T08:00:00'),
        (@t_final_report, @student4_id, @student1_id, '2026-12-01T08:05:00'),
        (@t_defense, @student1_id, @student1_id, '2026-12-15T08:00:00'),
        (@t_defense, @student3_id, @student1_id, '2026-12-15T08:05:00');

    INSERT INTO dbo.task_dependencies (task_id, depends_on_task_id, dependency_type)
    VALUES
        (@t_erd, @t_workflow, N'FINISH_TO_START'),
        (@t_auth, @t_erd, N'FINISH_TO_START'),
        (@t_project_api, @t_auth, N'FINISH_TO_START'),
        (@t_dashboard, @t_project_api, N'START_TO_START'),
        (@t_task_module, @t_project_api, N'FINISH_TO_START'),
        (@t_reports, @t_task_module, N'FINISH_TO_START'),
        (@t_files, @t_reports, N'START_TO_START'),
        (@t_final_report, @t_reports, N'FINISH_TO_START'),
        (@t_defense, @t_final_report, N'START_TO_START');

    INSERT INTO dbo.task_status_history
        (task_id, old_status, new_status, changed_by, reason, changed_at)
    VALUES
        (@t_workflow, NULL, N'TODO', @student1_id, N'Task created during milestone planning.', '2026-09-01T08:00:00'),
        (@t_workflow, N'TODO', N'IN_PROGRESS', @student1_id, N'Workflow analysis started.', '2026-09-01T09:00:00'),
        (@t_workflow, N'IN_PROGRESS', N'DONE', @student1_id, N'Core business workflow documented and reviewed by the team.', '2026-09-05T16:00:00'),
        (@t_erd, NULL, N'TODO', @student1_id, N'Database design task created.', '2026-09-04T09:00:00'),
        (@t_erd, N'TODO', N'IN_PROGRESS', @student2_id, N'ERD design and SQL Server constraints implementation started.', '2026-09-05T08:00:00'),
        (@t_erd, N'IN_PROGRESS', N'IN_REVIEW', @student2_id, N'Initial schema completed and sent for backend review.', '2026-09-09T14:00:00'),
        (@t_erd, N'IN_REVIEW', N'DONE', @student1_id, N'PK/FK, multi-major relationships, indexes, and state constraints reviewed.', '2026-09-10T17:00:00'),
        (@t_project_api, NULL, N'TODO', @student1_id, N'API implementation task created.', '2026-09-24T08:00:00'),
        (@t_project_api, N'TODO', N'IN_PROGRESS', @student2_id, N'Implementation started after authentication APIs stabilized.', '2026-09-25T08:00:00'),
        (@t_project_api, N'IN_PROGRESS', N'IN_REVIEW', @student2_id, N'Core team/project endpoints completed and pull request opened.', '2026-10-04T16:00:00'),
        (@t_project_api, N'IN_REVIEW', N'DONE', @student1_id, N'Pull request approved after API contract and database relationship review.', '2026-10-05T17:00:00'),
        (@t_dashboard, NULL, N'TODO', @student1_id, N'Dashboard task created after core API planning.', '2026-10-01T08:00:00'),
        (@t_dashboard, N'TODO', N'IN_PROGRESS', @student4_id, N'Frontend implementation started with project summary and task widgets.', '2026-10-02T09:00:00'),
        (@t_dashboard, N'IN_PROGRESS', N'DONE', @student4_id, N'Student dashboard connected to backend project and task endpoints.', '2026-10-14T16:30:00'),
        (@t_files, NULL, N'TODO', @student1_id, N'File repository integration task created.', '2026-11-08T08:00:00'),
        (@t_files, N'TODO', N'BLOCKED', @student5_id, N'Waiting for object-storage bucket credentials and agreed storage naming convention.', '2026-11-09T10:00:00');

    /* =========================================================
       7. PROGRESS REPORTS
       ========================================================= */

    INSERT INTO dbo.progress_reports
        (project_id, submitted_by, report_type, period_start, period_end, summary,
         completed_work, planned_work, issues_and_risks, status, submitted_at)
    VALUES
        (@project_id, @student1_id, N'WEEKLY', '2026-09-01', '2026-09-07',
         N'The team completed initial workflow analysis and started the SQL Server ERD for the non-AI core modules.',
         N'Collected business requirements; mapped actors and project lifecycle; drafted academic, identity, team, and project entities.',
         N'Finalize ERD, supervisor workflow, task/history design, and review constraints with backend members.',
         N'Business rules for one-team-one-project and project-level versus student-level evaluation still require team confirmation.',
         N'REVIEWED', '2026-09-08T09:00:00'),
        (@project_id, @student1_id, N'WEEKLY', '2026-09-08', '2026-09-14',
         N'The database design, proposal review, and supervisor matching workflow were finalized.',
         N'Completed schema design; confirmed multi-major project mapping; project approved; supervisor requests submitted.',
         N'Start ASP.NET Core Web API authentication/JWT implementation and set up the initial .NET backend project structure.',
         N'Need to ensure database workflow validations that remain application-owned are enforced in the ASP.NET Core application/service layer.',
         N'REVIEWED', '2026-09-15T09:00:00'),
        (@project_id, @student1_id, N'MONTHLY', '2026-09-01', '2026-09-30',
         N'September focused on requirements, database architecture, project approval, supervisor assignment, and authentication groundwork.',
         N'Project approved; two supervisors assigned; initial SQL Server schema completed; authentication/RBAC implementation substantially completed.',
         N'Complete team/project APIs, build dashboard, and start milestone/task execution features.',
         N'Potential schedule risk if backend API review and frontend integration occur sequentially rather than in parallel.',
         N'SUBMITTED', '2026-10-01T10:00:00');

    DECLARE @weekly_report1 BIGINT = (SELECT id FROM dbo.progress_reports WHERE project_id = @project_id AND report_type = N'WEEKLY' AND period_start = '2026-09-01');
    DECLARE @weekly_report2 BIGINT = (SELECT id FROM dbo.progress_reports WHERE project_id = @project_id AND report_type = N'WEEKLY' AND period_start = '2026-09-08');
    DECLARE @monthly_report BIGINT = (SELECT id FROM dbo.progress_reports WHERE project_id = @project_id AND report_type = N'MONTHLY' AND period_start = '2026-09-01');

    /* =========================================================
       8. MEETINGS AND PARTICIPANTS
       ========================================================= */

    INSERT INTO dbo.meetings
        (project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url, status, created_by)
    VALUES
        (@project_id, N'Project Kickoff with Supervisors',
         N'Confirm scope, supervision responsibilities, communication cadence, milestone plan, and database-first implementation strategy.',
         N'Primary supervisor will focus on architecture/backend/database. Co-supervisor will guide future AI modules. Core non-AI schema is prioritized first.',
         '2026-09-12T14:00:00', '2026-09-12T15:30:00', N'Gamma Building, FPT University - Da Nang Campus', NULL, N'COMPLETED', @student1_id),
        (@project_id, N'Database and Architecture Review',
         N'Review SQL Server entities, multi-major relationships, status/history tables, indexes, delete behavior, and backend validation responsibilities.',
         N'Agreed to keep AI-specific tables outside the initial schema. Requested stronger reference data and explicit smoke-test coverage for all core modules.',
         '2026-09-25T09:00:00', '2026-09-25T10:30:00', N'Alpha Building, FPT University - Da Nang Campus', NULL, N'COMPLETED', @student1_id),
        (@project_id, N'M3 Weekly Progress Sync',
         N'Review milestone/task module, progress reporting, file metadata, deliverables, and blockers.',
         N'Milestone/task module is in progress. File storage integration is blocked until object-storage configuration is confirmed.',
         '2026-11-10T15:00:00', '2026-11-10T16:00:00', N'Gamma Building, FPT University - Da Nang Campus', NULL, N'COMPLETED', @student1_id),
        (@project_id, N'Final Submission Readiness Review',
         N'Check final report, deployment evidence, testing report, evaluation completion, and defense preparation.',
         NULL,
         '2026-12-18T14:00:00', '2026-12-18T15:00:00', N'Alpha Building, FPT University - Da Nang Campus', NULL, N'SCHEDULED', @student1_id);

    DECLARE @meeting_kickoff BIGINT = (SELECT id FROM dbo.meetings WHERE project_id = @project_id AND title = N'Project Kickoff with Supervisors');
    DECLARE @meeting_arch BIGINT = (SELECT id FROM dbo.meetings WHERE project_id = @project_id AND title = N'Database and Architecture Review');
    DECLARE @meeting_weekly BIGINT = (SELECT id FROM dbo.meetings WHERE project_id = @project_id AND title = N'M3 Weekly Progress Sync');
    DECLARE @meeting_final BIGINT = (SELECT id FROM dbo.meetings WHERE project_id = @project_id AND title = N'Final Submission Readiness Review');

    INSERT INTO dbo.meeting_participants (meeting_id, user_id, attendance_status)
    VALUES
        (@meeting_kickoff, @lecturer1_id, N'ATTENDED'),
        (@meeting_kickoff, @lecturer2_id, N'ATTENDED'),
        (@meeting_kickoff, @student1_id, N'ATTENDED'),
        (@meeting_kickoff, @student2_id, N'ATTENDED'),
        (@meeting_kickoff, @student3_id, N'ATTENDED'),
        (@meeting_kickoff, @student4_id, N'ATTENDED'),
        (@meeting_kickoff, @student5_id, N'ATTENDED'),
        (@meeting_arch, @lecturer1_id, N'ATTENDED'),
        (@meeting_arch, @student1_id, N'ATTENDED'),
        (@meeting_arch, @student2_id, N'ATTENDED'),
        (@meeting_arch, @student4_id, N'ATTENDED'),
        (@meeting_weekly, @lecturer1_id, N'ATTENDED'),
        (@meeting_weekly, @student1_id, N'ATTENDED'),
        (@meeting_weekly, @student2_id, N'ATTENDED'),
        (@meeting_weekly, @student3_id, N'ATTENDED'),
        (@meeting_weekly, @student5_id, N'ATTENDED'),
        (@meeting_final, @lecturer1_id, N'ACCEPTED'),
        (@meeting_final, @lecturer2_id, N'ACCEPTED'),
        (@meeting_final, @student1_id, N'ACCEPTED'),
        (@meeting_final, @student2_id, N'INVITED'),
        (@meeting_final, @student3_id, N'INVITED'),
        (@meeting_final, @student4_id, N'INVITED'),
        (@meeting_final, @student5_id, N'INVITED');

    /* =========================================================
       9. DELIVERABLES AND VERSIONS
       ========================================================= */

    INSERT INTO dbo.deliverables
        (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by)
    VALUES
        (@project_id, @m1, N'Project Proposal', N'Approved project proposal describing context, objectives, scope, features, technology, and expected outputs.', N'PROPOSAL', '2026-09-15T18:00:00', N'ACCEPTED', @student1_id),
        (@project_id, @m2, N'Database Design Package', N'ERD, SQL Server schema, relationship notes, constraints, indexes, seed data, and recreate instructions.', N'TECHNICAL_DOCUMENT', '2026-10-10T18:00:00', N'ACCEPTED', @student1_id),
        (@project_id, @m3, N'M3 Progress Package', N'Progress report bundle containing task status, screenshots, API evidence, and collaboration-module implementation notes.', N'PROGRESS_PACKAGE', '2026-11-30T18:00:00', N'SUBMITTED', @student1_id),
        (@project_id, @m4, N'Final Project Report', N'Final technical report and supporting evidence for project submission and defense.', N'FINAL_REPORT', '2026-12-20T18:00:00', N'OPEN', @student1_id);

    DECLARE @deliv_proposal BIGINT = (SELECT id FROM dbo.deliverables WHERE project_id = @project_id AND title = N'Project Proposal');
    DECLARE @deliv_db BIGINT = (SELECT id FROM dbo.deliverables WHERE project_id = @project_id AND title = N'Database Design Package');
    DECLARE @deliv_m3 BIGINT = (SELECT id FROM dbo.deliverables WHERE project_id = @project_id AND title = N'M3 Progress Package');

    INSERT INTO dbo.deliverable_versions
        (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at)
    VALUES
        (@deliv_proposal, 1, @student1_id, N'Initial proposal submitted for supervisor and department comments.', N'REJECTED', '2026-09-08T15:00:00'),
        (@deliv_proposal, 2, @student1_id, N'Revised proposal addressing scope, multi-major rationale, milestone plan, and evaluation comments.', N'ACCEPTED', '2026-09-14T14:00:00'),
        (@deliv_db, 1, @student2_id, N'Initial ERD and SQL Server schema package including 35 core tables, constraints, indexes, and required role seed data.', N'ACCEPTED', '2026-10-09T16:00:00'),
        (@deliv_m3, 1, @student3_id, N'First M3 evidence package with milestone/task API screenshots and progress-report examples.', N'SUBMITTED', '2026-11-28T17:00:00');

    DECLARE @proposal_v1 BIGINT = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @deliv_proposal AND version_number = 1);
    DECLARE @proposal_v2 BIGINT = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @deliv_proposal AND version_number = 2);
    DECLARE @db_v1 BIGINT = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @deliv_db AND version_number = 1);
    DECLARE @m3_v1 BIGINT = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @deliv_m3 AND version_number = 1);

    /* =========================================================
       10. SUPERVISOR FEEDBACK
       ========================================================= */

    INSERT INTO dbo.supervisor_feedback
        (project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, feedback_text)
    VALUES
        (@project_id, @assignment1, @weekly_report1, NULL, NULL,
         N'Good initial progress. Keep the database scope aligned with the approved non-AI requirements and document any business rules that must remain in the backend service layer.'),
        (@project_id, @assignment1, NULL, @proposal_v1, NULL,
         N'Revise the proposal to make the multi-major contribution clearer and separate the initial core database scope from later AI-specific persistence requirements.'),
        (@project_id, @assignment2, NULL, NULL, @meeting_arch,
         N'The core schema is suitable for future AI extension. Preserve clean status history and progress-report data because these will later become useful inputs for risk prediction and summarization models.');

    DECLARE @feedback_report BIGINT = (
        SELECT id FROM dbo.supervisor_feedback
        WHERE project_id = @project_id AND progress_report_id = @weekly_report1
    );
    DECLARE @feedback_proposal BIGINT = (
        SELECT id FROM dbo.supervisor_feedback
        WHERE project_id = @project_id AND deliverable_version_id = @proposal_v1
    );
    DECLARE @feedback_meeting BIGINT = (
        SELECT id FROM dbo.supervisor_feedback
        WHERE project_id = @project_id AND meeting_id = @meeting_arch
    );

    /* =========================================================
       11. FILE METADATA
       Actual file bytes are intentionally NOT stored in SQL Server.
       ========================================================= */

    INSERT INTO dbo.files
        (uploaded_by, deliverable_version_id, progress_report_id, meeting_id, supervisor_feedback_id,
         original_file_name, stored_file_name, storage_path, file_url, mime_type, file_size_bytes, checksum_sha256)
    VALUES
        (@student1_id, @proposal_v2, NULL, NULL, NULL,
         N'AI-PMS_Project_Proposal_v2.pdf', N'aipms-proposal-v2-20260914.pdf',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/deliverables/proposal/v2/aipms-proposal-v2-20260914.pdf',
         NULL, N'application/pdf', 2457600,
         N'1111111111111111111111111111111111111111111111111111111111111111'),
        (@student2_id, @db_v1, NULL, NULL, NULL,
         N'AI-PMS_Database_ERD_and_Schema.zip', N'aipms-db-design-v1-20261009.zip',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/deliverables/database/v1/aipms-db-design-v1-20261009.zip',
         NULL, N'application/zip', 5242880,
         N'2222222222222222222222222222222222222222222222222222222222222222'),
        (@student1_id, NULL, @weekly_report1, NULL, NULL,
         N'Weekly_Report_Week01.pdf', N'weekly-report-20260901-20260907.pdf',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/progress/weekly/weekly-report-20260901-20260907.pdf',
         NULL, N'application/pdf', 786432,
         N'3333333333333333333333333333333333333333333333333333333333333333'),
        (@student4_id, NULL, NULL, @meeting_arch, NULL,
         N'Database_Architecture_Review_Minutes.docx', N'architecture-review-minutes-20260925.docx',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/meetings/20260925/architecture-review-minutes-20260925.docx',
         NULL, N'application/vnd.openxmlformats-officedocument.wordprocessingml.document', 196608,
         N'4444444444444444444444444444444444444444444444444444444444444444'),
        (@lecturer2_id, NULL, NULL, NULL, @feedback_meeting,
         N'AI_Data_Readiness_Feedback.txt', N'ai-data-readiness-feedback-20260925.txt',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/feedback/ai-data-readiness-feedback-20260925.txt',
         NULL, N'text/plain', 8192,
         N'5555555555555555555555555555555555555555555555555555555555555555'),
        (@student3_id, @m3_v1, NULL, NULL, NULL,
         N'M3_Progress_Evidence.zip', N'm3-progress-evidence-20261128.zip',
         N'/aipms/fa26/projects/SEP490-FA26-AIPMS/deliverables/m3/v1/m3-progress-evidence-20261128.zip',
         NULL, N'application/zip', 9437184,
         N'6666666666666666666666666666666666666666666666666666666666666666');

    /* =========================================================
       12. EVALUATION / RUBRICS

       evaluation_criteria:
         Global reusable criterion definitions.

       rubrics:
         A concrete scoring rubric for a department/semester.

       rubric_criteria:
         Defines weight_percent and max_score for each criterion
         inside a specific rubric.

       evaluations:
         Stores which rubric was used for a project evaluation.

       evaluation_details:
         Stores the actual score for each rubric criterion.
       ========================================================= */

    /* Reusable criterion catalog. */
    INSERT INTO dbo.evaluation_criteria
        (code, name, description, is_active)
    VALUES
        (N'REQ_ANALYSIS',
         N'Requirements and Problem Analysis',
         N'Clarity of problem definition, academic workflow understanding, scope, and requirements quality.',
         1),

        (N'ARCH_DESIGN',
         N'System and Database Architecture',
         N'Quality of software architecture, ERD, normalization, relationships, constraints, and extensibility.',
         1),

        (N'IMPLEMENT',
         N'Implementation Quality',
         N'Correctness, maintainability, security, API quality, testing, and integration of implemented core modules.',
         1),

        (N'COLLAB',
         N'Team Collaboration and Contribution',
         N'Task ownership, communication, progress transparency, meeting participation, and individual contribution.',
         1),

        (N'DOC_PRESENT',
         N'Documentation and Presentation',
         N'Quality of technical documentation, reports, deliverables, project explanation, and presentation.',
         1);

    DECLARE @c_req BIGINT =
        (SELECT id FROM dbo.evaluation_criteria WHERE code = N'REQ_ANALYSIS');

    DECLARE @c_arch BIGINT =
        (SELECT id FROM dbo.evaluation_criteria WHERE code = N'ARCH_DESIGN');

    DECLARE @c_impl BIGINT =
        (SELECT id FROM dbo.evaluation_criteria WHERE code = N'IMPLEMENT');

    DECLARE @c_collab BIGINT =
        (SELECT id FROM dbo.evaluation_criteria WHERE code = N'COLLAB');

    DECLARE @c_doc BIGINT =
        (SELECT id FROM dbo.evaluation_criteria WHERE code = N'DOC_PRESENT');


    /* SEP490 rubric for the IT department in FA26.
       Both department_id and academic_semester_id are supplied so the
       sample demonstrates a semester-specific department rubric.
    */
    INSERT INTO dbo.rubrics
        (department_id, academic_semester_id, code, version_number, name, description,
         approval_status, is_active, created_by, approved_by, approved_at)
    VALUES
        (@it_dept_id,
         @semester_fa26,
         N'SEP490_FA26_IT',
         1,
         N'SEP490 Fall 2026 - IT Project Evaluation Rubric',
         N'Initial SEP490 evaluation rubric used to assess AI-PMS and other IT capstone projects in the Fall 2026 semester.',
         N'APPROVED',
         1,
         @staff_id,
         @staff_id,
         '2026-08-25T10:00:00');

    DECLARE @rubric_sep490 BIGINT =
        (SELECT id FROM dbo.rubrics WHERE code = N'SEP490_FA26_IT');


    /* Criterion configuration belongs to the rubric, not the global criterion.
       Total weight = 100%.
    */
    INSERT INTO dbo.rubric_criteria
        (rubric_id, criterion_id, weight_percent, max_score, sort_order, is_required)
    VALUES
        (@rubric_sep490, @c_req,    20.00, 10.00, 1, 1),
        (@rubric_sep490, @c_arch,   20.00, 10.00, 2, 1),
        (@rubric_sep490, @c_impl,   30.00, 10.00, 3, 1),
        (@rubric_sep490, @c_collab, 15.00, 10.00, 4, 1),
        (@rubric_sep490, @c_doc,    15.00, 10.00, 5, 1);

    DECLARE @rc_req BIGINT =
        (SELECT id FROM dbo.rubric_criteria
         WHERE rubric_id = @rubric_sep490 AND criterion_id = @c_req);

    DECLARE @rc_arch BIGINT =
        (SELECT id FROM dbo.rubric_criteria
         WHERE rubric_id = @rubric_sep490 AND criterion_id = @c_arch);

    DECLARE @rc_impl BIGINT =
        (SELECT id FROM dbo.rubric_criteria
         WHERE rubric_id = @rubric_sep490 AND criterion_id = @c_impl);

    DECLARE @rc_collab BIGINT =
        (SELECT id FROM dbo.rubric_criteria
         WHERE rubric_id = @rubric_sep490 AND criterion_id = @c_collab);

    DECLARE @rc_doc BIGINT =
        (SELECT id FROM dbo.rubric_criteria
         WHERE rubric_id = @rubric_sep490 AND criterion_id = @c_doc);


    /* Multiple evaluators may evaluate the same project with the same rubric. */
    INSERT INTO dbo.evaluations
        (project_id, evaluator_id, rubric_id, evaluation_type,
         status, total_score, comments, evidence_summary, evaluated_at,
         finalized_by, finalized_at, rounding_rule)
    VALUES
        (@project_id,
         @lecturer1_id,
         @rubric_sep490,
         N'SUPERVISOR',
         N'FINALIZED',
         8.71,
         N'Strong core database design and project workflow coverage. Continue improving automated tests and complete collaboration modules before final submission.',
         N'Rubric scores are supported by reviewed progress reports, accepted deliverables, meeting minutes, and supervisor observations.',
         '2026-11-30T16:00:00',
         @lecturer1_id,
         '2026-11-30T16:00:00',
         N'AWAY_FROM_ZERO_2DP'),

        (@project_id,
         @lecturer2_id,
         @rubric_sep490,
         N'LECTURER',
         N'SUBMITTED',
         8.42,
         N'Good preparation for future AI extensions. Progress-report and status-history data are structured well for later analytics use.',
         N'Reviewed database artifacts, reports, and submitted project evidence.',
         '2026-11-30T16:30:00',
         NULL,
         NULL,
         N'AWAY_FROM_ZERO_2DP'),

        (@project_id,
         @staff_id,
         @rubric_sep490,
         N'COMMITTEE',
         N'DRAFT',
         NULL,
         N'Final committee evaluation will be completed after the SEP490 project defense.',
         NULL,
         NULL,
         NULL,
         NULL,
         N'AWAY_FROM_ZERO_2DP');

    DECLARE @eval_sup BIGINT =
        (SELECT id
         FROM dbo.evaluations
         WHERE project_id = @project_id
           AND evaluator_id = @lecturer1_id
           AND evaluation_type = N'SUPERVISOR');

    DECLARE @eval_lect BIGINT =
        (SELECT id
         FROM dbo.evaluations
         WHERE project_id = @project_id
           AND evaluator_id = @lecturer2_id
           AND evaluation_type = N'LECTURER');


    /* Actual score values reference rubric_criteria. */
    INSERT INTO dbo.evaluation_details
        (evaluation_id, rubric_criterion_id, score, comments)
    VALUES
        (@eval_sup,  @rc_req,    9.00, N'Business scope and project lifecycle are clearly modeled.'),
        (@eval_sup,  @rc_arch,   9.20, N'Good normalized schema, clear junction tables, history preservation, and indexing strategy.'),
        (@eval_sup,  @rc_impl,   8.50, N'Core backend is progressing well; more end-to-end automated tests are recommended.'),
        (@eval_sup,  @rc_collab, 8.30, N'Team task allocation is visible and cross-major contribution is clearly documented.'),
        (@eval_sup,  @rc_doc,    8.50, N'Documentation is clear; keep README and database assumptions synchronized with code.'),

        (@eval_lect, @rc_req,    8.80, N'Multi-disciplinary problem and AI extension direction are well motivated.'),
        (@eval_lect, @rc_arch,   8.70, N'Core data model is suitable for adding AI analytics tables later.'),
        (@eval_lect, @rc_impl,   8.10, N'Current implementation is solid but AI modules are intentionally deferred.'),
        (@eval_lect, @rc_collab, 8.40, N'Team includes members from multiple majors with appropriate task distribution.'),
        (@eval_lect, @rc_doc,    8.20, N'Reports and deliverables are consistent and traceable.');

    /* =========================================================
       13. NOTIFICATIONS
       ========================================================= */

    INSERT INTO dbo.notifications
        (created_by, notification_type, title, content, related_entity_type, related_entity_id)
    VALUES
        (@staff_id, N'PROJECT_STATUS', N'Project approved',
         N'Your AI-PMS project proposal has been approved and moved to supervisor assignment.', N'PROJECT', @project_id),
        (@lecturer1_id, N'SUPERVISOR_ASSIGNMENT', N'Primary supervisor assigned',
         N'Lecturer Nguyễn Hoàng Minh has been assigned as the primary supervisor for AI-PMS.', N'PROJECT', @project_id),
        (@student1_id, N'TASK_ASSIGNED', N'New task assignment',
         N'You have been assigned to the milestone/task module implementation.', N'TASK', @t_task_module),
        (@lecturer1_id, N'PROGRESS_FEEDBACK', N'Weekly progress feedback available',
         N'Feedback has been added to the first weekly progress report.', N'PROGRESS_REPORT', @weekly_report1),
        (@student3_id, N'DELIVERABLE_SUBMITTED', N'M3 progress package submitted',
         N'Version 1 of the M3 Progress Package has been submitted for review.', N'DELIVERABLE', @deliv_m3),
        (@student1_id, N'MEETING_REMINDER', N'Final readiness review scheduled',
         N'Final Submission Readiness Review is scheduled for 18 December 2026 at 14:00.', N'MEETING', @meeting_final);

    DECLARE @n_project BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'PROJECT' AND title = N'Project approved');
    DECLARE @n_supervisor BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'PROJECT' AND title = N'Primary supervisor assigned');
    DECLARE @n_task BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'TASK' AND related_entity_id = @t_task_module);
    DECLARE @n_feedback BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'PROGRESS_REPORT' AND related_entity_id = @weekly_report1);
    DECLARE @n_deliv BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'DELIVERABLE' AND related_entity_id = @deliv_m3);
    DECLARE @n_meeting BIGINT = (SELECT id FROM dbo.notifications WHERE related_entity_type = N'MEETING' AND related_entity_id = @meeting_final);

    INSERT INTO dbo.notification_recipients
        (notification_id, user_id, is_read, read_at, delivered_at)
    VALUES
        (@n_project, @student1_id, 1, '2026-09-10T16:05:00', '2026-09-10T15:31:00'),
        (@n_project, @student2_id, 1, '2026-09-10T16:20:00', '2026-09-10T15:31:00'),
        (@n_project, @student3_id, 0, NULL, '2026-09-10T15:31:00'),
        (@n_project, @student4_id, 1, '2026-09-10T17:00:00', '2026-09-10T15:31:00'),
        (@n_project, @student5_id, 0, NULL, '2026-09-10T15:31:00'),
        (@n_supervisor, @student1_id, 1, '2026-09-12T09:10:00', '2026-09-12T09:06:00'),
        (@n_supervisor, @lecturer1_id, 1, '2026-09-12T09:15:00', '2026-09-12T09:06:00'),
        (@n_task, @student2_id, 1, '2026-10-16T08:10:00', '2026-10-16T08:06:00'),
        (@n_task, @student3_id, 0, NULL, '2026-10-16T08:06:00'),
        (@n_feedback, @student1_id, 1, '2026-09-08T12:00:00', '2026-09-08T11:45:00'),
        (@n_deliv, @lecturer1_id, 0, NULL, '2026-11-28T17:01:00'),
        (@n_deliv, @lecturer2_id, 0, NULL, '2026-11-28T17:01:00'),
        (@n_meeting, @lecturer1_id, 1, '2026-12-15T09:00:00', '2026-12-15T08:00:00'),
        (@n_meeting, @lecturer2_id, 1, '2026-12-15T09:05:00', '2026-12-15T08:00:00'),
        (@n_meeting, @student1_id, 1, '2026-12-15T09:10:00', '2026-12-15T08:00:00'),
        (@n_meeting, @student2_id, 0, NULL, '2026-12-15T08:00:00'),
        (@n_meeting, @student3_id, 0, NULL, '2026-12-15T08:00:00'),
        (@n_meeting, @student4_id, 0, NULL, '2026-12-15T08:00:00'),
        (@n_meeting, @student5_id, 0, NULL, '2026-12-15T08:00:00');

    COMMIT TRANSACTION;

    PRINT N'AI-PMS detailed sample data inserted successfully.';
    PRINT N'Reference data loaded successfully. Local development accounts use password Aipms@123.';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO

/* =========================================================
   SAMPLE DATA COVERAGE CHECK
   Every initial table should return a row count > 0 after
   schema.sql + seed.sql + sample_data.sql.
   ========================================================= */

SELECT N'organizations' AS table_name, COUNT_BIG(*) AS row_count FROM dbo.organizations
UNION ALL SELECT N'departments', COUNT_BIG(*) FROM dbo.departments
UNION ALL SELECT N'majors', COUNT_BIG(*) FROM dbo.majors
UNION ALL SELECT N'academic_semesters', COUNT_BIG(*) FROM dbo.academic_semesters
UNION ALL SELECT N'project_periods', COUNT_BIG(*) FROM dbo.project_periods
UNION ALL SELECT N'roles', COUNT_BIG(*) FROM dbo.roles
UNION ALL SELECT N'users', COUNT_BIG(*) FROM dbo.users
UNION ALL SELECT N'user_roles', COUNT_BIG(*) FROM dbo.user_roles
UNION ALL SELECT N'teams', COUNT_BIG(*) FROM dbo.teams
UNION ALL SELECT N'team_members', COUNT_BIG(*) FROM dbo.team_members
UNION ALL SELECT N'team_invitations', COUNT_BIG(*) FROM dbo.team_invitations
UNION ALL SELECT N'projects', COUNT_BIG(*) FROM dbo.projects
UNION ALL SELECT N'project_majors', COUNT_BIG(*) FROM dbo.project_majors
UNION ALL SELECT N'project_status_history', COUNT_BIG(*) FROM dbo.project_status_history
UNION ALL SELECT N'supervisor_profiles', COUNT_BIG(*) FROM dbo.supervisor_profiles
UNION ALL SELECT N'supervisor_expertise', COUNT_BIG(*) FROM dbo.supervisor_expertise
UNION ALL SELECT N'supervisor_requests', COUNT_BIG(*) FROM dbo.supervisor_requests
UNION ALL SELECT N'supervisor_assignments', COUNT_BIG(*) FROM dbo.supervisor_assignments
UNION ALL SELECT N'milestones', COUNT_BIG(*) FROM dbo.milestones
UNION ALL SELECT N'tasks', COUNT_BIG(*) FROM dbo.tasks
UNION ALL SELECT N'task_assignees', COUNT_BIG(*) FROM dbo.task_assignees
UNION ALL SELECT N'task_dependencies', COUNT_BIG(*) FROM dbo.task_dependencies
UNION ALL SELECT N'task_status_history', COUNT_BIG(*) FROM dbo.task_status_history
UNION ALL SELECT N'progress_reports', COUNT_BIG(*) FROM dbo.progress_reports
UNION ALL SELECT N'meetings', COUNT_BIG(*) FROM dbo.meetings
UNION ALL SELECT N'meeting_participants', COUNT_BIG(*) FROM dbo.meeting_participants
UNION ALL SELECT N'deliverables', COUNT_BIG(*) FROM dbo.deliverables
UNION ALL SELECT N'deliverable_versions', COUNT_BIG(*) FROM dbo.deliverable_versions
UNION ALL SELECT N'files', COUNT_BIG(*) FROM dbo.files
UNION ALL SELECT N'supervisor_feedback', COUNT_BIG(*) FROM dbo.supervisor_feedback
UNION ALL SELECT N'evaluation_criteria', COUNT_BIG(*) FROM dbo.evaluation_criteria
UNION ALL SELECT N'rubrics', COUNT_BIG(*) FROM dbo.rubrics
UNION ALL SELECT N'rubric_criteria', COUNT_BIG(*) FROM dbo.rubric_criteria
UNION ALL SELECT N'evaluations', COUNT_BIG(*) FROM dbo.evaluations
UNION ALL SELECT N'evaluation_details', COUNT_BIG(*) FROM dbo.evaluation_details
UNION ALL SELECT N'notifications', COUNT_BIG(*) FROM dbo.notifications
UNION ALL SELECT N'notification_recipients', COUNT_BIG(*) FROM dbo.notification_recipients
ORDER BY table_name;
GO
