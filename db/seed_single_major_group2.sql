/* ==============================================================================================
   SCRIPT: seed_single_major_group2.sql
   MỤC ĐÍCH: Khởi tạo Nhóm Đồ án Đơn ngành thứ 2 (SINGLE_MAJOR) - Trạng thái ACTIVE
   DỰ ÁN: SEP490-FA26-SE02 - Hệ thống Giám sát Hiệu năng và Cảnh báo Sự cố Vi dịch vụ (Observability)
   NHÓM:  SEP490_G04 (Chuyên ngành Kỹ thuật Phần mềm - SE)
   GVHD:  ThS. Nguyễn Hoàng Minh (lecturer.se01@fe.edu.vn)
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

DECLARE @TargetProjectCode nvarchar(50) = N'SEP490-FA26-SE02';
DECLARE @TargetTeamCode nvarchar(50) = N'SEP490_G04';

IF EXISTS (SELECT 1 FROM dbo.projects WHERE code = @TargetProjectCode)
BEGIN
    PRINT N': Dự án đơn ngành "' + @TargetProjectCode + N'" đã tồn tại trong cơ sở dữ liệu. Script kết thúc an toàn!';
    RETURN;
END;

BEGIN TRANSACTION;

-- 1. Xác định ngữ cảnh học thuật động
DECLARE @SemesterId bigint = (
    SELECT TOP 1 id FROM dbo.academic_semesters 
    WHERE status = N'ACTIVE' 
    ORDER BY id DESC
);
IF @SemesterId IS NULL
    SET @SemesterId = (SELECT TOP 1 id FROM dbo.academic_semesters WHERE code = N'FA26');
IF @SemesterId IS NULL SET @SemesterId = 1;

DECLARE @ExecutionPeriodId bigint = (
    SELECT TOP 1 id FROM dbo.project_periods 
    WHERE academic_semester_id = @SemesterId AND period_type = N'EXECUTION'
);
IF @ExecutionPeriodId IS NULL
    SET @ExecutionPeriodId = (SELECT TOP 1 id FROM dbo.project_periods WHERE academic_semester_id = @SemesterId AND code LIKE N'%EXEC%');
IF @ExecutionPeriodId IS NULL
    SET @ExecutionPeriodId = (SELECT TOP 1 id FROM dbo.project_periods WHERE code = N'SEP490-FA26-EXEC');
IF @ExecutionPeriodId IS NULL
    SET @ExecutionPeriodId = 3;

DECLARE @MajorSeId bigint = (SELECT TOP 1 id FROM dbo.majors WHERE code = N'SE');
IF @MajorSeId IS NULL
    SET @MajorSeId = (SELECT TOP 1 id FROM dbo.majors WHERE code LIKE N'%SE%');

DECLARE @DeptItId bigint = (SELECT department_id FROM dbo.majors WHERE id = @MajorSeId);
IF @DeptItId IS NULL
    SET @DeptItId = (SELECT TOP 1 id FROM dbo.departments WHERE code = N'IT');

DECLARE @SupervisorUserId bigint = (SELECT TOP 1 id FROM dbo.users WHERE email = N'lecturer.se01@fe.edu.vn');
IF @SupervisorUserId IS NULL
    SET @SupervisorUserId = (SELECT TOP 1 id FROM dbo.users WHERE email = N'lecturer.se02@fe.edu.vn');
DECLARE @SupervisorProfileId bigint = (SELECT TOP 1 id FROM dbo.supervisor_profiles WHERE user_id = @SupervisorUserId);

DECLARE @CoordinatorUserId bigint = (SELECT TOP 1 id FROM dbo.users WHERE email = N'sep490.coordinator@fpt.edu.vn');
IF @CoordinatorUserId IS NULL
    SET @CoordinatorUserId = (SELECT TOP 1 u.id FROM dbo.users u JOIN dbo.user_roles ur ON ur.user_id = u.id JOIN dbo.roles r ON r.id = ur.role_id WHERE r.code = N'DEPARTMENT_STAFF');
IF @CoordinatorUserId IS NULL SET @CoordinatorUserId = 2;

DECLARE @RoleStudentId bigint = (SELECT TOP 1 id FROM dbo.roles WHERE code = N'STUDENT');
DECLARE @RoleLecturerId bigint = (SELECT TOP 1 id FROM dbo.roles WHERE code = N'LECTURER');
DECLARE @DefaultPasswordHash nvarchar(500) = N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==';

-- 2. Tạo 4 sinh viên SE
-- Sinh viên 1: Trưởng nhóm (Leader) - Hoàng Văn Nam
DECLARE @LeaderUserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180205' OR email = N'namhvse180205@fpt.edu.vn');
IF @LeaderUserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'namhvse180205@fpt.edu.vn', @DefaultPasswordHash, N'Hoàng Văn Nam',
        N'0905111225', N'SE180205', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @LeaderUserId = SCOPE_IDENTITY();
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@LeaderUserId, @RoleStudentId);
END;

-- Sinh viên 2: Bùi Thanh Thảo (Frontend Lead)
DECLARE @Member2UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180206' OR email = N'thaobtse180206@fpt.edu.vn');
IF @Member2UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'thaobtse180206@fpt.edu.vn', @DefaultPasswordHash, N'Bùi Thanh Thảo',
        N'0905111226', N'SE180206', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member2UserId = SCOPE_IDENTITY();
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member2UserId, @RoleStudentId);
END;

-- Sinh viên 3: Đỗ Tuấn Kiệt (Cloud & SRE Lead)
DECLARE @Member3UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180207' OR email = N'kietdtse180207@fpt.edu.vn');
IF @Member3UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'kietdtse180207@fpt.edu.vn', @DefaultPasswordHash, N'Đỗ Tuấn Kiệt',
        N'0905111227', N'SE180207', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member3UserId = SCOPE_IDENTITY();
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member3UserId, @RoleStudentId);
END;

-- Sinh viên 4: Vũ Hải Yến (Data & Monitoring Lead)
DECLARE @Member4UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180208' OR email = N'yenvhse180208@fpt.edu.vn');
IF @Member4UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'yenvhse180208@fpt.edu.vn', @DefaultPasswordHash, N'Vũ Hải Yến',
        N'0905111228', N'SE180208', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member4UserId = SCOPE_IDENTITY();
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member4UserId, @RoleStudentId);
END;

-- 3. Tạo Nhóm SEP490_G04
DECLARE @TeamId bigint = (SELECT id FROM dbo.teams WHERE academic_semester_id = @SemesterId AND code = @TargetTeamCode);
IF @TeamId IS NULL
BEGIN
    INSERT INTO dbo.teams (
        academic_semester_id, code, name, description, status, created_by, created_at, updated_at
    )
    VALUES (
        @SemesterId, @TargetTeamCode, 
        N'Nhóm Đồ án G04 - Chuyên ngành Kỹ thuật Phần mềm (SE)',
        N'Nhóm sinh viên thực hiện đồ án tốt nghiệp chuyên ngành Kỹ thuật Phần mềm (SE), đề tài Hệ thống Giám sát Hiệu năng và Cảnh báo Sự cố Vi dịch vụ Theo Thời gian thực.',
        N'LOCKED', @LeaderUserId, '2026-08-18 08:30:00', '2026-09-16 16:00:00'
    );
    SET @TeamId = SCOPE_IDENTITY();
END;

-- Thêm thành viên vào nhóm
DECLARE @GenLeader uniqueidentifier = NEWID();
DECLARE @GenM2 uniqueidentifier = NEWID();
DECLARE @GenM3 uniqueidentifier = NEWID();
DECLARE @GenM4 uniqueidentifier = NEWID();

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @LeaderUserId)
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @LeaderUserId, 1, '2026-08-18 09:00:00', NULL, '2026-08-18 09:00:00', '2026-08-18 09:00:00', @GenLeader);

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member2UserId)
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member2UserId, 0, '2026-08-18 09:15:00', NULL, '2026-08-18 09:15:00', '2026-08-18 09:15:00', @GenM2);

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member3UserId)
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member3UserId, 0, '2026-08-18 09:20:00', NULL, '2026-08-18 09:20:00', '2026-08-18 09:20:00', @GenM3);

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member4UserId)
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member4UserId, 0, '2026-08-18 09:30:00', NULL, '2026-08-18 09:30:00', '2026-08-18 09:30:00', @GenM4);

DECLARE @TeamMemberLeaderId bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @LeaderUserId);
DECLARE @TeamMemberM2Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member2UserId);
DECLARE @TeamMemberM3Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member3UserId);
DECLARE @TeamMemberM4Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member4UserId);

SET @GenLeader = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberLeaderId);
SET @GenM2 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM2Id);
SET @GenM3 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM3Id);
SET @GenM4 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM4Id);

-- Cấu hình học thuật nhóm: SINGLE_MAJOR
IF NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id = @TeamId)
BEGIN
    INSERT INTO dbo.team_academic_configurations (
        team_id, project_mode, primary_major_id, lead_department_id, concurrency_token, responsibility_version
    )
    VALUES (
        @TeamId, N'SINGLE_MAJOR', @MajorSeId, @DeptItId, NEWID(), NEWID()
    );
END;

IF NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id = @TeamId AND major_id = @MajorSeId)
BEGIN
    INSERT INTO dbo.team_major_requirements (team_id, major_id, min_members, max_members, responsibility)
    VALUES (
        @TeamId, @MajorSeId, 3, 5,
        N'Toàn bộ sinh viên thuộc chuyên ngành Kỹ thuật Phần mềm (SE), chịu trách nhiệm thiết kế kiến trúc vi dịch vụ, pipeline thu thập metric OpenTelemetry, distributed tracing và engine phát hiện bất thường.'
    );
END;

-- 4. Tạo Đồ án SEP490-FA26-SE02 (ACTIVE)
DECLARE @ProjectId bigint = (SELECT id FROM dbo.projects WHERE code = @TargetProjectCode);
IF @ProjectId IS NULL
BEGIN
    INSERT INTO dbo.projects (
        team_id, code, title, description, objectives, status,
        registered_at, submitted_at, approved_at, completed_at, created_by,
        created_at, updated_at, problem_statement, expected_output, milestones_initialized
    )
    VALUES (
        @TeamId, @TargetProjectCode,
        N'Hệ thống Giám sát Hiệu năng và Cảnh báo Sự cố Vi dịch vụ Theo Thời gian thực (Real-time Microservices Observability & Incident Detection Platform)',
        N'Nền tảng quan sát toàn diện (Observability) thu thập Metrics, Logs, Traces theo chuẩn OpenTelemetry, tích hợp mô hình phân tích chuỗi thời gian để cảnh báo sự cố vi dịch vụ theo thời gian thực.',
        N'1. Thu thập dữ liệu đo kiểm phân tán độ trễ < 50ms.\n2. Phát hiện sớm nghẽn mạng và quá tải tài nguyên đạt độ chính xác > 92%.\n3. Trực quan hóa topology vi dịch vụ và timeline sự cố trên dashboard trực quan.',
        N'ACTIVE',
        '2026-08-22 09:00:00', '2026-08-29 15:00:00', '2026-09-14 10:00:00', NULL, @LeaderUserId,
        '2026-08-22 09:00:00', GETUTCDATE(),
        N'Hệ thống vi dịch vụ phân tán quy mô lớn thường xuyên gặp sự cố cascade failure và khó xác định nguyên nhân gốc rễ (root cause) khi không có distributed tracing thống nhất.',
        N'Bộ công cụ OpenTelemetry Collector, Prometheus metrics store, Grafana-like dashboard, Rule-based & AI anomaly detector, và webhook gửi cảnh báo đa kênh (Slack, Telegram, Email).',
        1
    );
    SET @ProjectId = SCOPE_IDENTITY();
END;

-- Project major
IF NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id = @ProjectId AND major_id = @MajorSeId)
BEGIN
    INSERT INTO dbo.project_majors (project_id, major_id, created_at)
    VALUES (@ProjectId, @MajorSeId, GETUTCDATE());
END;

-- project_major_requirements cho dự án đơn ngành SE
IF NOT EXISTS (SELECT 1 FROM dbo.project_major_requirements WHERE project_id = @ProjectId AND major_id = @MajorSeId)
BEGIN
    INSERT INTO dbo.project_major_requirements (
        project_id, major_id, min_members, max_members, responsibility, concurrency_token, created_at, updated_at
    )
    VALUES (
        @ProjectId, @MajorSeId, 3, 5,
        N'Đảm nhiệm toàn bộ vòng đời phát triển phần mềm (SDLC), kiến trúc hệ thống vi dịch vụ, pipeline thu thập dữ liệu OpenTelemetry, backend ASP.NET Core, frontend React, CI/CD pipeline và kiểm thử tải.',
        NEWID(), GETUTCDATE(), GETUTCDATE()
    );
END;

-- Lịch sử phê duyệt đồ án
INSERT INTO dbo.project_status_history (project_id, old_status, new_status, changed_by, reason, changed_at)
VALUES
(@ProjectId, NULL, N'DRAFT', @LeaderUserId, N'Khởi tạo hồ sơ đề tài vi dịch vụ', '2026-08-22 09:00:00'),
(@ProjectId, N'DRAFT', N'SUBMITTED', @LeaderUserId, N'Nộp đề cương chi tiết đề tài', '2026-08-29 15:00:00'),
(@ProjectId, N'SUBMITTED', N'UNDER_REVIEW', @CoordinatorUserId, N'Hội đồng Bộ môn tiếp nhận thẩm định', '2026-09-03 10:00:00'),
(@ProjectId, N'UNDER_REVIEW', N'APPROVED', @CoordinatorUserId, N'Bộ môn CNTT thông qua đề tài đơn ngành SE', '2026-09-14 10:00:00'),
(@ProjectId, N'APPROVED', N'SUPERVISOR_PENDING', @CoordinatorUserId, N'Gửi yêu cầu hướng dẫn đến giảng viên', '2026-09-14 10:30:00'),
(@ProjectId, N'SUPERVISOR_PENDING', N'ACTIVE', @SupervisorUserId, N'GVHD chấp thuận hướng dẫn chính; kích hoạt đồ án vào kỳ làm việc', '2026-09-16 16:00:00');

-- Yêu cầu giảng viên hướng dẫn (Supervisor Request)
DECLARE @SupervisorRequestId bigint;
INSERT INTO dbo.supervisor_requests (
    project_id, supervisor_profile_id, requested_by, assignment_type, major_id, status,
    request_message, response_message, requested_at, expires_at, responded_at, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorProfileId, @LeaderUserId, N'PRIMARY', NULL, N'ACCEPTED',
    N'Kính gửi Thầy Nguyễn Hoàng Minh, Nhóm G04 kính mong Thầy nhận hướng dẫn đề tài Hệ thống Giám sát Hiệu năng Vi dịch vụ.',
    N'Đề tài có tính thực tiễn cao về Cloud-native và Observability. Thầy đồng ý hướng dẫn chính nhóm G04.',
    '2026-09-14 10:30:00', '2026-09-21 10:30:00', '2026-09-16 16:00:00', '2026-09-14 10:30:00', '2026-09-16 16:00:00'
);
SET @SupervisorRequestId = SCOPE_IDENTITY();

-- Phân công Giảng viên Hướng dẫn chính
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_assignments WHERE project_id = @ProjectId AND supervisor_profile_id = @SupervisorProfileId)
BEGIN
    INSERT INTO dbo.supervisor_assignments (
        project_id, supervisor_profile_id, supervisor_request_id, is_primary, assignment_type,
        major_id, assigned_by, assigned_at, ended_at, created_at, updated_at
    )
    VALUES (
        @ProjectId, @SupervisorProfileId, @SupervisorRequestId, 1, N'PRIMARY',
        NULL, @CoordinatorUserId, '2026-09-16 16:00:00', NULL, '2026-09-16 16:00:00', '2026-09-16 16:00:00'
    );
END;

-- 6. Thỏa ước làm việc nhóm (Working Agreement)
DECLARE @WorkingAgreementId bigint;
IF NOT EXISTS (SELECT 1 FROM dbo.project_working_agreements WHERE project_id = @ProjectId)
BEGIN
    INSERT INTO dbo.project_working_agreements (
        project_id, version, status, content_json, concurrency_token, created_by, created_at, published_at
    )
    VALUES (
        @ProjectId, 1, N'ACTIVE',
        N'{"rules":["Cam kết dự Daily Scrum đúng 9:00 sáng mỗi ngày qua Discord.","Task quá hạn 24h tự động gắn thẻ Needs Help.","Mọi Pull Request bắt buộc có ít nhất 1 Reviewer duyệt trước khi merge."],"communication":"Discord and Zalo Group","meetingSchedule":"Thứ 3 và Thứ 6 hàng tuần lúc 14:00"}',
        NEWID(), @LeaderUserId, '2026-09-16 17:00:00', '2026-09-16 17:00:00'
    );
    SET @WorkingAgreementId = SCOPE_IDENTITY();

    INSERT INTO dbo.agreement_members (agreement_id, user_id, team_member_id, membership_joined_at, membership_generation, accepted_at)
    VALUES
    (@WorkingAgreementId, @LeaderUserId, @TeamMemberLeaderId, '2026-08-18 09:00:00', @GenLeader, '2026-09-16 17:30:00'),
    (@WorkingAgreementId, @Member2UserId, @TeamMemberM2Id, '2026-08-18 09:15:00', @GenM2, '2026-09-16 17:45:00'),
    (@WorkingAgreementId, @Member3UserId, @TeamMemberM3Id, '2026-08-18 09:20:00', @GenM3, '2026-09-16 18:00:00'),
    (@WorkingAgreementId, @Member4UserId, @TeamMemberM4Id, '2026-08-18 09:30:00', @GenM4, '2026-09-16 18:15:00');
END;

-- 7. 4 Mốc Milestones
DECLARE @M1Id bigint, @M2Id bigint, @M3Id bigint, @M4Id bigint;
INSERT INTO dbo.milestones (project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token)
VALUES
(@ProjectId, N'Giai đoạn 1: Đặc tả Yêu cầu & Kiến trúc Thu thập Metric OpenTelemetry', N'Hoàn thiện tài liệu kiến trúc, khảo sát hệ sinh thái OpenTelemetry, benchmark dung lượng lưu trữ.', '2026-09-16', '2026-09-30', N'COMPLETED', 1, @LeaderUserId, '2026-09-16', '2026-09-30', NEWID()),
(@ProjectId, N'Giai đoạn 2: Phát triển Core Collector & Pipeline Xử lý Dòng Log/Trace', N'Xây dựng OpenTelemetry Collector custom, streaming logs qua Kafka, index vào ClickHouse/Elasticsearch.', '2026-10-01', '2026-10-18', N'COMPLETED', 2, @LeaderUserId, '2026-10-01', '2026-10-18', NEWID()),
(@ProjectId, N'Giai đoạn 3: Dashboard Trực quan & Thuật toán Phát hiện Bất thường', N'Trực quan hóa Topology vi dịch vụ, đo lường SLI/SLO và phát hiện anomaly trên độ trễ p99.', '2026-10-19', '2026-11-10', N'IN_PROGRESS', 3, @LeaderUserId, '2026-10-19', GETUTCDATE(), NEWID()),
(@ProjectId, N'Giai đoạn 4: Đánh giá Tải diện rộng, Kiểm thử Bảo mật & Bảo vệ Đồ án', N'Thực hiện stress test 50,000 req/s, hoàn thiện báo cáo tốt nghiệp chuẩn IEEE và slide bảo vệ.', '2026-11-11', '2026-11-30', N'PLANNED', 4, @LeaderUserId, '2026-11-11', GETUTCDATE(), NEWID());

SET @M1Id = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 1);
SET @M2Id = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 2);
SET @M3Id = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 3);
SET @M4Id = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 4);

-- 8. 3 Sprints
DECLARE @Sp1Id bigint, @Sp2Id bigint, @Sp3Id bigint;
INSERT INTO dbo.sprints (project_id, name, status, start_at, end_at, started_at, completed_at, concurrency_token, created_by, created_at, updated_at)
VALUES
(@ProjectId, N'Sprint 1: Kiến trúc Pipeline & Core Ingestion', N'COMPLETED', '2026-09-16 00:00:00', '2026-09-30 23:59:59', '2026-09-16 08:00:00', '2026-09-30 18:00:00', NEWID(), @LeaderUserId, '2026-09-16', '2026-09-30'),
(@ProjectId, N'Sprint 2: Distributed Tracing & Topology Graph', N'ACTIVE', '2026-10-01 00:00:00', '2026-10-28 23:59:59', '2026-10-01 08:00:00', NULL, NEWID(), @LeaderUserId, '2026-10-01', GETUTCDATE()),
(@ProjectId, N'Sprint 3: Anomaly Detection Engine & Multi-channel Alerting', N'PLANNED', '2026-10-29 00:00:00', '2026-11-15 23:59:59', NULL, NULL, NEWID(), @LeaderUserId, '2026-10-29', GETUTCDATE());

SET @Sp1Id = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'%Sprint 1%');
SET @Sp2Id = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'%Sprint 2%');
SET @Sp3Id = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'%Sprint 3%');

-- 9. 12 Kanban Tasks
DECLARE @Tasks TABLE (
    idx int identity(1,1),
    title nvarchar(300),
    description nvarchar(1000),
    status nvarchar(20),
    priority nvarchar(20),
    story_points int,
    milestone_id bigint,
    sprint_id bigint,
    assignee_id bigint,
    label varchar(50)
);

INSERT INTO @Tasks VALUES
-- Sprint 1 (DONE)
(N'Khảo sát và benchmark OpenTelemetry Collector với Kafka', N'Đo lường throughput và memory footprint khi ingest 10.000 log events/giây.', N'DONE', N'HIGH', 5, @M1Id, @Sp1Id, @LeaderUserId, 'Backend'),
(N'Thiết kế sơ đồ kiến trúc vi dịch vụ & Data Flow Diagram', N'Mô tả chi tiết flow đẩy telemetry data từ SDK qua agent đến time-series store.', N'DONE', N'MEDIUM', 3, @M1Id, @Sp1Id, @Member2UserId, 'Frontend'),
(N'Triển khai cụm Kafka & ClickHouse trên môi trường staging', N'Cấu hình replication factor = 2 và partitioning theo service_name.', N'DONE', N'HIGH', 8, @M1Id, @Sp1Id, @Member3UserId, 'DevOps'),
(N'Đặc tả tài liệu API Ingestion Contract theo chuẩn OTLP', N'Xây dựng Proto schema cho custom telemetry metrics và log payload.', N'DONE', N'MEDIUM', 3, @M1Id, @Sp1Id, @Member4UserId, 'Testing'),
-- Sprint 2 (IN_PROGRESS & IN_REVIEW)
(N'Phát triển API dựng sơ đồ Topology Vi dịch vụ động', N'Truy vấn distributed traces để vẽ dependency graph giữa các service theo thời gian thực.', N'IN_REVIEW', N'HIGH', 5, @M2Id, @Sp2Id, @LeaderUserId, 'Backend'),
(N'Thiết kế UI Trace Timeline và Waterfall Chart', N'Trực quan hóa waterfall chi tiết của từng trace ID kèm span metadata và error flags.', N'IN_PROGRESS', N'HIGH', 8, @M2Id, @Sp2Id, @Member2UserId, 'Frontend'),
(N'Cấu hình bộ thu thập Metric Prometheus Exporter cho Kubernetes', N'Tự động cào node metrics, pod CPU/RAM và container network I/O.', N'DONE', N'MEDIUM', 5, @M2Id, @Sp2Id, @Member3UserId, 'DevOps'),
(N'Viết bộ kiểm thử tải sinh giả lập sự cố Microservices', N'Dùng k6 sinh 20.000 user đồng thời, tiêm lỗi HTTP 500 và network latency spike 500ms.', N'IN_PROGRESS', N'HIGH', 5, @M3Id, @Sp2Id, @Member4UserId, 'Testing'),
-- Sprint 3 (TODO & PLANNED)
(N'Xây dựng thuật toán phân tích chuỗi thời gian phát hiện Anomaly', N'Ứng dụng EWMA và Z-score động để phát hiện spike đột biến trên metric độ trễ.', N'TODO', N'HIGH', 8, @M3Id, @Sp3Id, @LeaderUserId, 'Backend'),
(N'Giao diện cấu hình Alert Rules và Thresholds', N'Cho phép người dùng tạo rule cảnh báo linh hoạt theo cú pháp PromQL trực quan.', N'TODO', N'MEDIUM', 5, @M3Id, @Sp3Id, @Member2UserId, 'Frontend'),
(N'Tích hợp Webhook cảnh báo qua Slack, Telegram và PagerDuty', N'Đảm bảo thời gian từ lúc phát hiện sự cố đến khi bắn tin nhắn < 3 giây.', N'TODO', N'MEDIUM', 3, @M3Id, @Sp3Id, @Member3UserId, 'DevOps'),
(N'Bộ tài liệu hướng dẫn vận hành và Runbook xử lý sự cố', N'Đóng gói tài liệu bàn giao, playbook phục hồi thảm họa và hướng dẫn tích hợp SDK.', N'TODO', N'LOW', 2, @M4Id, @Sp3Id, @Member4UserId, 'Testing');

DECLARE @cur_title nvarchar(300), @cur_desc nvarchar(1000), @cur_status nvarchar(20), @cur_priority nvarchar(20);
DECLARE @cur_sp int, @cur_m bigint, @cur_sp_id bigint, @cur_assignee bigint, @cur_label varchar(50);

DECLARE task_cursor CURSOR LOCAL FORWARD_ONLY READ_ONLY FOR
SELECT title, description, status, priority, story_points, milestone_id, sprint_id, assignee_id, label FROM @Tasks;
OPEN task_cursor;
FETCH NEXT FROM task_cursor INTO @cur_title, @cur_desc, @cur_status, @cur_priority, @cur_sp, @cur_m, @cur_sp_id, @cur_assignee, @cur_label;

WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO dbo.tasks (
        milestone_id, parent_task_id, title, description, status, priority,
        start_at, due_at, completed_at, created_by, created_at, updated_at,
        concurrency_token, sprint_id, story_points
    )
    VALUES (
        @cur_m, NULL, @cur_title, @cur_desc, @cur_status, @cur_priority,
        '2026-10-01', '2026-10-28', CASE WHEN @cur_status = 'DONE' THEN '2026-10-15' ELSE NULL END,
        @LeaderUserId, '2026-10-01', GETUTCDATE(), NEWID(), @cur_sp_id, @cur_sp
    );
    DECLARE @NewTaskId bigint = SCOPE_IDENTITY();
    INSERT INTO dbo.task_assignees (task_id, user_id, assigned_by, assigned_at) VALUES (@NewTaskId, @cur_assignee, @LeaderUserId, '2026-10-01');

    FETCH NEXT FROM task_cursor INTO @cur_title, @cur_desc, @cur_status, @cur_priority, @cur_sp, @cur_m, @cur_sp_id, @cur_assignee, @cur_label;
END;
CLOSE task_cursor;
DEALLOCATE task_cursor;

-- 10. 4 Meetings
INSERT INTO dbo.meetings (
    project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url,
    status, created_by, created_at, updated_at, meeting_delivery_mode, video_channel,
    concurrency_token, recurrence, notes_version
)
VALUES
(@ProjectId, N'Họp Kick-off Đồ án & Thống nhất Công nghệ Thu thập Telemetry', N'Chốt danh mục công nghệ: OpenTelemetry, Kafka, ClickHouse, Grafana UI.', N'Thầy Nguyễn Hoàng Minh nhất trí định hướng thiết kế. Yêu cầu nhóm chú trọng chuẩn OTLP để tránh phụ thuộc vendor.', '2026-09-18 14:00:00', '2026-09-18 15:30:00', N'Phòng Lab 402 - ĐH FPT', NULL, N'COMPLETED', @LeaderUserId, '2026-09-17', '2026-09-18 16:00:00', 'ONSITE', 'NONE', NEWID(), N'NONE', 0),
(@ProjectId, N'Họp Đánh giá Tiến độ Sprint 1 & Review Kiến trúc Thu thập', N'Trình chiếu pipeline ingest mẫu đạt 10.000 metric/s không nghẽn Kafka.', N'Kiến trúc pipeline đạt chuẩn. Thầy Minh góp ý tối ưu kích thước batch trước khi ghi vào ClickHouse.', '2026-09-30 14:00:00', '2026-09-30 15:15:00', NULL, N'https://livekit.aipms.fe.edu.vn/room/sep490-se02-sp1', N'COMPLETED', @LeaderUserId, '2026-09-29', '2026-09-30 16:00:00', 'REMOTE', 'EXTERNAL_LINK', NEWID(), N'NONE', 0),
(@ProjectId, N'Họp Định kỳ Sprint 2 - Demo Waterfall Tracing & Dependency Graph', N'Demo trực quan cây phân rã thời gian xử lý vi dịch vụ và graph topology.', N'Giao diện waterfall rất trực quan. Cần bổ sung filter lọc theo HTTP status code và thời gian phản hồi.', '2026-10-16 14:00:00', '2026-10-16 15:30:00', N'Phòng Họp 204 Beta', N'https://livekit.aipms.fe.edu.vn/room/sep490-se02-sp2', N'COMPLETED', @LeaderUserId, '2026-10-15', '2026-10-16 16:00:00', 'HYBRID', 'EXTERNAL_LINK', NEWID(), N'NONE', 0),
(@ProjectId, N'Họp Dự kiến: Báo cáo Giải thuật Phát hiện Bất thường (Anomaly Detection)', N'Đánh giá hiệu quả của thuật toán Z-Score và so sánh với mô hình Isolation Forest.', NULL, '2026-10-30 14:00:00', '2026-10-30 15:30:00', NULL, N'https://livekit.aipms.fe.edu.vn/room/sep490-se02-upcoming', N'SCHEDULED', @LeaderUserId, GETUTCDATE(), GETUTCDATE(), 'REMOTE', 'EXTERNAL_LINK', NEWID(), N'NONE', 0);

-- Điểm danh họp
INSERT INTO dbo.meeting_participants (meeting_id, user_id, attendance_status, created_at, updated_at, rsvp_status, rsvp_token)
SELECT m.id, u.id, N'ATTENDED', GETUTCDATE(), GETUTCDATE(), N'ACCEPTED', NEWID()
FROM dbo.meetings m
CROSS JOIN (
    SELECT @LeaderUserId as id UNION ALL SELECT @Member2UserId UNION ALL SELECT @Member3UserId UNION ALL SELECT @Member4UserId UNION ALL SELECT @SupervisorUserId
) u
WHERE m.project_id = @ProjectId;

-- 11. 3 Báo cáo tiến độ và nhận xét của GVHD
DECLARE @SupervisorAssignmentId bigint = (SELECT TOP 1 id FROM dbo.supervisor_assignments WHERE project_id = @ProjectId);

-- Chu kỳ 1: Tuần 2 (WEEKLY) - 16/09 đến 23/09
INSERT INTO dbo.progress_report_periods (
    project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @ExecutionPeriodId, N'WEEKLY', '2026-09-16 00:00:00', '2026-09-23 23:59:59', '2026-09-24 18:00:00', N'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()
);
DECLARE @Period1Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.progress_reports (
    project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work,
    issues_and_risks, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id,
    is_late, in_progress_work, blockers, risks, next_actions
)
VALUES (
    @ProjectId, @LeaderUserId, N'WEEKLY', '2026-09-16', '2026-09-23',
    N'Báo cáo tiến độ tuần 2: Hoàn thành thiết kế hệ thống và thiết lập hạ tầng pipeline thu thập OTLP.',
    N'- Hoàn thiện SRS v1.0 và Software Architecture Document.\n- Dựng cụm Kafka và ClickHouse trên Docker Swarm.\n- Đo lường throughput đạt 12.000 log events/giây.',
    N'- Triển khai cụm phân tán tracing và tích hợp UI waterfall.',
    N'Chưa có rủi ro lớn.', N'REVIEWED', '2026-09-23 17:00:00', '2026-09-23', '2026-09-24 09:00:00', NEWID(), @Period1Id,
    0, N'Tối ưu kích thước batch ingest', NULL, NULL, N'Bắt đầu Sprint 2 đúng kế hoạch'
);
DECLARE @R1Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.supervisor_feedback (
    project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, gate_code,
    feedback_text, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorAssignmentId, @R1Id, NULL, NULL, NULL,
    N'Nhóm làm việc nghiêm túc, bám sát tiến độ Sprint 1. Kiến trúc phân tầng và giải pháp ClickHouse rất phù hợp cho Big Data telemetry.',
    '2026-09-24 09:30:00', '2026-09-24 09:30:00'
);

-- Chu kỳ 2: Tháng 9 (MONTHLY) - 01/09 đến 30/09
INSERT INTO dbo.progress_report_periods (
    project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @ExecutionPeriodId, N'MONTHLY', '2026-09-01 00:00:00', '2026-09-30 23:59:59', '2026-10-02 18:00:00', N'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()
);
DECLARE @Period2Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.progress_reports (
    project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work,
    issues_and_risks, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id,
    is_late, in_progress_work, blockers, risks, next_actions
)
VALUES (
    @ProjectId, @LeaderUserId, N'MONTHLY', '2026-09-01', '2026-09-30',
    N'Báo cáo tiến độ đợt 2: Hoàn thành API Trace Topology và UI Waterfall Chart.',
    N'- API render service dependency graph đạt độ trễ < 80ms.\n- UI Waterfall hoàn thiện 90% chức năng zoom và search.\n- Tích hợp Prometheus metrics exporter cho Kubernetes node.',
    N'- Hoàn thiện engine phát hiện anomaly và webhook cảnh báo.',
    N'Khối lượng trace data tăng nhanh cần cấu hình chính sách TTL retention 14 ngày.', N'REVIEWED', '2026-10-01 17:00:00', '2026-10-01', '2026-10-02 09:00:00', NEWID(), @Period2Id,
    0, N'Tích hợp k6 load test script', NULL, NULL, N'Đẩy mạnh Sprint 2 và chuẩn bị kiểm thử tải'
);
DECLARE @R2Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.supervisor_feedback (
    project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, gate_code,
    feedback_text, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorAssignmentId, @R2Id, NULL, NULL, NULL,
    N'Giao diện Waterfall trace rất ấn tượng và chuyên nghiệp. Nhóm cần chú ý cấu hình data retention hợp lý để tránh tràn ổ đĩa khi test tải lớn.',
    '2026-10-02 09:30:00', '2026-10-02 09:30:00'
);

-- Chu kỳ 3: Tuần 6 (WEEKLY) - 15/10 đến 22/10 (SUBMITTED)
INSERT INTO dbo.progress_report_periods (
    project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @ExecutionPeriodId, N'WEEKLY', '2026-10-15 00:00:00', '2026-10-22 23:59:59', '2026-10-23 18:00:00', N'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()
);
DECLARE @Period3Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.progress_reports (
    project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work,
    issues_and_risks, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id,
    is_late, in_progress_work, blockers, risks, next_actions
)
VALUES (
    @ProjectId, @LeaderUserId, N'WEEKLY', '2026-10-15', '2026-10-22',
    N'Báo cáo tiến độ đợt 3: Đang thực hiện kịch bản kiểm thử tải diện rộng và anomaly detection.',
    N'- Đã viết xong 8 kịch bản k6 tiêm lỗi microservices.\n- Hoàn thiện thuật toán Z-score anomaly detector cơ bản.',
    N'- Kết nối webhook Telegram/Slack cảnh báo và đóng gói Docker Compose 1-click.',
    N'Cần môi trường có RAM tối thiểu 16GB để chạy đồng thời 10 microservices và ClickHouse.', N'SUBMITTED', '2026-10-22 10:00:00', '2026-10-22', GETUTCDATE(), NEWID(), @Period3Id,
    0, N'Tối ưu thuật toán anomaly p99', NULL, NULL, N'Chờ thầy Minh phản hồi đợt 3'
);

-- 12. 4 Deliverables
DECLARE @Del1Id bigint, @Del2Id bigint, @Del3Id bigint;
INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at)
VALUES
(@ProjectId, @M1Id, N'Tài liệu Đặc tả Yêu cầu Hệ thống (SRS v1.0)', N'Bản đặc tả chi tiết 25 Use Cases, Data Dictionary và kiến trúc OTLP.', N'DOCUMENT', '2026-09-30 23:59:59', N'SUBMITTED', @LeaderUserId, '2026-09-28', '2026-09-30'),
(@ProjectId, @M1Id, N'Tài liệu Thiết kế Kiến trúc Hệ thống (SAD v1.0)', N'Sơ đồ Component, Sequence Diagram, Database ERD và chính sách bảo mật.', N'DOCUMENT', '2026-09-30 23:59:59', N'SUBMITTED', @LeaderUserId, '2026-09-29', '2026-09-30'),
(@ProjectId, @M2Id, N'Gói Mã nguồn Telemetry Ingestion Pipeline (v1.0-alpha)', N'Source code OpenTelemetry Collector, Kafka stream processor và ClickHouse sink.', N'SOURCE_CODE', '2026-10-18 23:59:59', N'SUBMITTED', @LeaderUserId, '2026-10-17', '2026-10-18'),
(@ProjectId, @M3Id, N'Bản dựng Thử nghiệm Anomaly Detector & Dashboard (v1.0-beta)', N'Gói triển khai Docker Compose chứa Dashboard UI và Python Anomaly Worker.', N'SOURCE_CODE', '2026-11-10 23:59:59', N'OPEN', @LeaderUserId, '2026-10-20', GETUTCDATE());

SET @Del1Id = (SELECT id FROM dbo.deliverables WHERE project_id = @ProjectId AND title LIKE N'%SRS%');
SET @Del2Id = (SELECT id FROM dbo.deliverables WHERE project_id = @ProjectId AND title LIKE N'%SAD%');
SET @Del3Id = (SELECT id FROM dbo.deliverables WHERE project_id = @ProjectId AND title LIKE N'%Pipeline%');

INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
VALUES
(@Del1Id, 1, @LeaderUserId, N'Nộp SRS hoàn thiện có xác nhận của cả nhóm', N'ACCEPTED', '2026-09-30 16:00:00', '2026-09-30', '2026-10-02'),
(@Del2Id, 1, @LeaderUserId, N'Bản thiết kế kiến trúc hệ thống SAD v1.0', N'ACCEPTED', '2026-09-30 16:30:00', '2026-09-30', '2026-10-02'),
(@Del3Id, 1, @LeaderUserId, N'Gói mã nguồn backend pipeline v1.0-alpha', N'ACCEPTED', '2026-10-18 16:00:00', '2026-10-18', '2026-10-20');

COMMIT TRANSACTION;

PRINT N'======================================================================================';
PRINT N'ĐÃ TẠO THÀNH CÔNG NHÓM ĐƠN NGÀNH THỨ 2: ' + @TargetTeamCode + N' (PROJECT: ' + @TargetProjectCode + N')!';
PRINT N'HỌC KỲ:   ID ' + CAST(@SemesterId AS NVARCHAR(10));
PRINT N'TRƯỞNG NHÓM: Hoàng Văn Nam (namhvse180205@fpt.edu.vn - SE180205)';
PRINT N'GVHD:     ThS. Nguyễn Hoàng Minh (lecturer.se01@fe.edu.vn)';
PRINT N'TRẠNG THÁI: Nhóm LOCKED - Đồ án ACTIVE';
PRINT N'======================================================================================';
