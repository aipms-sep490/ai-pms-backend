/* ==============================================================================================
   SCRIPT: seed_single_major_active_project.sql
   MỤC ĐÍCH: Khởi tạo nhóm đồ án đang trong quá trình thực hiện (ACTIVE) theo luồng ĐƠN NGÀNH (Single-Major)
   CHUYÊN NGÀNH: Kỹ thuật Phần mềm (Software Engineering - SE)
   HỆ THỐNG: AI-PMS (AI Project Management System) - FPT University Da Nang
   KỲ HỌC: FA26 (Fall 2026)
   
   ĐẶC ĐIỂM LUỒNG ĐƠN NGÀNH (SINGLE-MAJOR WORKFLOW):
     1. Nhóm sinh viên 100% thuộc chuyên ngành Kỹ thuật Phần mềm (SE).
     2. Cấu hình học thuật nhóm: project_mode = 'SINGLE_MAJOR', primary_major_id = SE.
     3. Bảng project_majors: CHỈ DUY NHẤT 1 bản ghi liên kết đến major SE (major_id = 1).
     4. Giảng viên hướng dẫn: Duy nhất 1 Giảng viên Hướng dẫn chính (PRIMARY) chuyên ngành SE (ThS. Đỗ Quang Huy),
        major_id = NULL (không có Mentor bộ môn chéo / DISCIPLINE_MENTOR vì là đề tài đơn ngành).
     5. Lịch sử phê duyệt (Audit Trail): Đầy đủ các bước từ Khởi tạo (DRAFT) -> Nộp đề cương (SUBMITTED)
        -> Bộ môn thẩm định (UNDER_REVIEW) -> Bộ môn phê duyệt (APPROVED) -> Kết nối GVHD (SUPERVISOR_PENDING)
        -> GVHD đồng ý & Bộ môn kích hoạt vào giai đoạn thực hiện (ACTIVE).
     6. Dữ liệu thực thi phong phú (In-Progress State):
        - Thỏa ước nhóm (Working Agreement) đã ký duyệt bởi 4 thành viên.
        - 4 Giai đoạn (Milestones): M1 (Xong), M2 (Xong), M3 (Đang làm), M4 (Kế hoạch).
        - 3 Sprints Agile: Sprint 1 (COMPLETED), Sprint 2 (ACTIVE), Sprint 3 (PLANNED).
        - Nhiệm vụ Kanban (Tasks) phân bổ đều cho 4 thành viên: TODO, IN_PROGRESS, IN_REVIEW, DONE kèm Story Points.
        - Lịch họp & Biên bản họp (Meetings, Attendees, Decisions, Action Items).
        - Báo cáo tiến độ chu kỳ (Progress Reports) & Nhận xét đánh giá của GVHD (Supervisor Feedback).
        - Sản phẩm bàn giao (Deliverables & Versions) kèm file đính kèm mô phỏng cho UI.
   
   AN TOÀN DỮ LIỆU & BẢO TOÀN DỮ LIỆU HIỆN HỮU:
     - Tuyệt đối KHÔNG chạm đến Project 1 (AIPMS) và Project 2 (LOGISTICS).
     - Tuyệt đối KHÔNG sửa đổi các nhóm và tài khoản hiện có.
     - Script có tính IDEMPOTENT: Nếu project 'SEP490-FA26-SE01' đã tồn tại, script tự động dừng an toàn.
     - 100% văn bản tiếng Việt có dấu chuẩn Unicode (N'...').
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

-- ==============================================================================================
-- 0. KIỂM TRA ĐIỀU KIỆN TIÊN QUYẾT & BẢO VỆ DỮ LIỆU (IDEMPOTENCY GUARD)
-- ==============================================================================================
DECLARE @TargetProjectCode nvarchar(50) = N'SEP490-FA26-SE01';
DECLARE @TargetTeamCode nvarchar(50) = N'SEP490_G03';

IF EXISTS (SELECT 1 FROM dbo.projects WHERE code = @TargetProjectCode)
BEGIN
    PRINT N'[THÔNG BÁO]: Dự án đơn ngành "' + @TargetProjectCode + N'" đã tồn tại trong cơ sở dữ liệu. Script kết thúc an toàn, không ghi đè dữ liệu!';
    RETURN;
END

BEGIN TRANSACTION;

PRINT N'--------------------------------------------------------------------------------------';
PRINT N'BẮT ĐẦU KHỞI TẠO DỰ ÁN ĐỒ ÁN ĐƠN NGÀNH SE: ' + @TargetProjectCode;
PRINT N'--------------------------------------------------------------------------------------';

-- ==============================================================================================
-- 1. XÁC ĐỊNH THÔNG TIN NỀN TẢNG (ACADEMIC BASE DATA)
-- ==============================================================================================
DECLARE @OrgId bigint = (SELECT TOP 1 id FROM dbo.organizations WHERE code = N'FPTU_DN');
IF @OrgId IS NULL SET @OrgId = (SELECT TOP 1 id FROM dbo.organizations ORDER BY id);

DECLARE @DeptItId bigint = (SELECT TOP 1 id FROM dbo.departments WHERE code = N'IT');
DECLARE @MajorSeId bigint = (SELECT TOP 1 id FROM dbo.majors WHERE code = N'SE');

-- Tự động ưu tiên học kỳ đang ACTIVE (ví dụ DST-20261024 trên web deploy, hoặc FA26)
DECLARE @SemesterId bigint = (
    SELECT TOP 1 id FROM dbo.academic_semesters 
    WHERE status = N'ACTIVE' 
    ORDER BY id DESC
);
IF @SemesterId IS NULL
    SET @SemesterId = (SELECT TOP 1 id FROM dbo.academic_semesters WHERE code = N'FA26');
IF @SemesterId IS NULL 
    SET @SemesterId = 1;

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

DECLARE @RoleStudentId bigint = (SELECT id FROM dbo.roles WHERE code = N'STUDENT');
DECLARE @RoleStaffId bigint = (SELECT id FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF');
DECLARE @RoleLecturerId bigint = (SELECT id FROM dbo.roles WHERE code = N'LECTURER');

-- Cán bộ Bộ môn phê duyệt (Coordinator Lê Thu Hà - ID 20 hoặc staff IT)
DECLARE @CoordinatorUserId bigint = (
    SELECT TOP 1 u.id FROM dbo.users u
    JOIN dbo.user_roles ur ON u.id = ur.user_id
    WHERE ur.role_id = @RoleStaffId AND u.email = N'sep490.coordinator@fpt.edu.vn'
);
IF @CoordinatorUserId IS NULL
    SET @CoordinatorUserId = (
        SELECT TOP 1 u.id FROM dbo.users u
        JOIN dbo.user_roles ur ON u.id = ur.user_id
        WHERE ur.role_id = @RoleStaffId
    );

-- Giảng viên Hướng dẫn chính: ThS. Đỗ Quang Huy (lecturer.se02@fe.edu.vn, Profile 5)
DECLARE @SupervisorUserId bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.se02@fe.edu.vn');
DECLARE @SupervisorProfileId bigint = (SELECT id FROM dbo.supervisor_profiles WHERE user_id = @SupervisorUserId);

-- Nếu chưa có profile GVHD cho user 13 thì tạo mới
IF @SupervisorProfileId IS NULL AND @SupervisorUserId IS NOT NULL
BEGIN
    INSERT INTO dbo.supervisor_profiles (user_id, bio, max_active_projects, is_available)
    VALUES (@SupervisorUserId, N'Thạc sĩ Kỹ thuật Phần mềm. Chuyên gia về Kiểm thử Phần mềm Tự động, Kiến trúc Hệ thống Phân tán, Microservices và Quy trình Agile CI/CD.', 5, 1);
    SET @SupervisorProfileId = SCOPE_IDENTITY();
END

-- Đảm bảo chuyên môn của GVHD được ghi nhận
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_expertise WHERE supervisor_profile_id = @SupervisorProfileId AND expertise_name = N'Software Testing & QA Automation')
BEGIN
    INSERT INTO dbo.supervisor_expertise (supervisor_profile_id, expertise_name, proficiency_level, created_at, updated_at)
    VALUES 
        (@SupervisorProfileId, N'Software Testing & QA Automation', N'EXPERT', GETUTCDATE(), GETUTCDATE()),
        (@SupervisorProfileId, N'Microservices & Distributed Systems', N'EXPERT', GETUTCDATE(), GETUTCDATE()),
        (@SupervisorProfileId, N'Agile CI/CD Pipelines', N'ADVANCED', GETUTCDATE(), GETUTCDATE());
END

PRINT N'1. Nền tảng học thuật: Kỳ ' + CAST(@SemesterId AS nvarchar(10)) + N', Chuyên ngành SE (ID: ' + CAST(@MajorSeId AS nvarchar(10)) + N'), GVHD: Đỗ Quang Huy (Profile: ' + CAST(@SupervisorProfileId AS nvarchar(10)) + N')';

-- ==============================================================================================
-- 2. TẠO 4 TÀI KHOẢN SINH VIÊN KỸ THUẬT PHẦN MỀM (SE STUDENTS - UNICODE CHUẨN)
-- ==============================================================================================
PRINT N'2. Khởi tạo 4 tài khoản sinh viên chuyên ngành Kỹ thuật Phần mềm (SE)...';

-- Mật khẩu mặc định cho môi trường dev/staging: Aipms@123
DECLARE @DefaultPasswordHash nvarchar(500) = N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==';

-- Sinh viên 1: Trưởng nhóm (Leader) - Nguyễn Văn Hùng
DECLARE @LeaderUserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180201' OR email = N'hungnvse180201@fpt.edu.vn');
IF @LeaderUserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'hungnvse180201@fpt.edu.vn', @DefaultPasswordHash, N'Nguyễn Văn Hùng',
        N'0905111221', N'SE180201', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @LeaderUserId = SCOPE_IDENTITY();

    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@LeaderUserId, @RoleStudentId);
END

-- Sinh viên 2: Thành viên - Trần Thị Mai Phương
DECLARE @Member2UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180202' OR email = N'phuongttmse180202@fpt.edu.vn');
IF @Member2UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'phuongttmse180202@fpt.edu.vn', @DefaultPasswordHash, N'Trần Thị Mai Phương',
        N'0905111222', N'SE180202', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member2UserId = SCOPE_IDENTITY();

    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member2UserId, @RoleStudentId);
END

-- Sinh viên 3: Thành viên - Lê Quang Huy
DECLARE @Member3UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180203' OR email = N'huylqse180203@fpt.edu.vn');
IF @Member3UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'huylqse180203@fpt.edu.vn', @DefaultPasswordHash, N'Lê Quang Huy',
        N'0905111223', N'SE180203', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member3UserId = SCOPE_IDENTITY();

    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member3UserId, @RoleStudentId);
END

-- Sinh viên 4: Thành viên - Phạm Hải Đăng
DECLARE @Member4UserId bigint = (SELECT id FROM dbo.users WHERE student_code = N'SE180204' OR email = N'dangphse180204@fpt.edu.vn');
IF @Member4UserId IS NULL
BEGIN
    INSERT INTO dbo.users (
        department_id, major_id, email, password_hash, full_name, phone, student_code, curriculum_code,
        google_enrollment_pending, status, academic_profile_status, access_failed_count, created_at, updated_at
    )
    VALUES (
        @DeptItId, @MajorSeId, N'dangphse180204@fpt.edu.vn', @DefaultPasswordHash, N'Phạm Hải Đăng',
        N'0905111224', N'SE180204', N'SE-FA26', 0, N'ACTIVE', N'VERIFIED', 0, GETUTCDATE(), GETUTCDATE()
    );
    SET @Member4UserId = SCOPE_IDENTITY();

    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@Member4UserId, @RoleStudentId);
END

PRINT N'-> Đã sẵn sàng 4 sinh viên: ' + CAST(@LeaderUserId AS nvarchar(10)) + N' (Hùng - Leader), ' 
      + CAST(@Member2UserId AS nvarchar(10)) + N' (Phương), ' + CAST(@Member3UserId AS nvarchar(10)) + N' (Huy), ' 
      + CAST(@Member4UserId AS nvarchar(10)) + N' (Đăng)';

-- ==============================================================================================
-- 3. KHỞI TẠO NHÓM ĐỒ ÁN (TEAM) & GÁN THÀNH VIÊN
-- ==============================================================================================
PRINT N'3. Khởi tạo nhóm đồ án đơn ngành: ' + @TargetTeamCode + N'...';

DECLARE @TeamId bigint = (SELECT id FROM dbo.teams WHERE academic_semester_id = @SemesterId AND code = @TargetTeamCode);
IF @TeamId IS NULL
BEGIN
    INSERT INTO dbo.teams (
        academic_semester_id, code, name, description, status, created_by, created_at, updated_at
    )
    VALUES (
        @SemesterId, @TargetTeamCode, 
        N'Nhóm Đồ án G03 - Chuyên ngành Kỹ thuật Phần mềm (SE)',
        N'Nhóm sinh viên thực hiện đồ án tốt nghiệp chuyên ngành Kỹ thuật Phần mềm (SE), đề tài Nền tảng Tự động hóa Kiểm thử và Đánh giá Chất lượng Phần mềm Phân tán.',
        N'LOCKED', @LeaderUserId, '2026-08-16 08:30:00', '2026-09-15 16:00:00'
    );
    SET @TeamId = SCOPE_IDENTITY();
END

-- Cấu hình học thuật nhóm: SINGLE_MAJOR, primary_major_id = SE, lead_department_id = IT
IF NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id = @TeamId)
BEGIN
    INSERT INTO dbo.team_academic_configurations (
        team_id, project_mode, primary_major_id, lead_department_id, concurrency_token, responsibility_version
    )
    VALUES (
        @TeamId, 'SINGLE_MAJOR', @MajorSeId, @DeptItId, NEWID(), NEWID()
    );
END

-- Yêu cầu chuyên môn của nhóm: SE (3-5 thành viên)
IF NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id = @TeamId AND major_id = @MajorSeId)
BEGIN
    INSERT INTO dbo.team_major_requirements (team_id, major_id, min_members, max_members, responsibility)
    VALUES (
        @TeamId, @MajorSeId, 3, 5, 
        N'Toàn bộ sinh viên thuộc chuyên ngành Kỹ thuật Phần mềm (SE), chịu trách nhiệm toàn diện về khảo sát yêu cầu, thiết kế kiến trúc hệ thống, phát triển backend, frontend, kiểm thử tự động phân tán và tích hợp CI/CD.'
    );
END

-- Thêm 4 thành viên vào team_members (nếu chưa có)
DECLARE @GenLeader uniqueidentifier = NEWID();
DECLARE @GenM2 uniqueidentifier = NEWID();
DECLARE @GenM3 uniqueidentifier = NEWID();
DECLARE @GenM4 uniqueidentifier = NEWID();

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @LeaderUserId)
BEGIN
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @LeaderUserId, 1, '2026-08-16 09:00:00', NULL, '2026-08-16 09:00:00', '2026-08-16 09:00:00', @GenLeader);
END

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member2UserId)
BEGIN
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member2UserId, 0, '2026-08-16 09:15:00', NULL, '2026-08-16 09:15:00', '2026-08-16 09:15:00', @GenM2);
END

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member3UserId)
BEGIN
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member3UserId, 0, '2026-08-16 09:20:00', NULL, '2026-08-16 09:20:00', '2026-08-16 09:20:00', @GenM3);
END

IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member4UserId)
BEGIN
    INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, left_at, created_at, updated_at, membership_generation)
    VALUES (@TeamId, @SemesterId, @Member4UserId, 0, '2026-08-16 09:30:00', NULL, '2026-08-16 09:30:00', '2026-08-16 09:30:00', @GenM4);
END

DECLARE @TeamMemberLeaderId bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @LeaderUserId);
DECLARE @TeamMemberM2Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member2UserId);
DECLARE @TeamMemberM3Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member3UserId);
DECLARE @TeamMemberM4Id bigint = (SELECT id FROM dbo.team_members WHERE team_id = @TeamId AND user_id = @Member4UserId);

-- Lấy lại membership generation thực tế
SET @GenLeader = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberLeaderId);
SET @GenM2 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM2Id);
SET @GenM3 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM3Id);
SET @GenM4 = (SELECT membership_generation FROM dbo.team_members WHERE id = @TeamMemberM4Id);

PRINT N'-> Nhóm ID: ' + CAST(@TeamId AS nvarchar(10)) + N' đã được tạo với 4 thành viên.';

-- ==============================================================================================
-- 4. KHỞI TẠO DỰ ÁN ĐỒ ÁN (PROJECT) & ĐẶC TẢ ĐƠN NGÀNH
-- ==============================================================================================
PRINT N'4. Khởi tạo đồ án: ' + @TargetProjectCode + N'...';

INSERT INTO dbo.projects (
    team_id, code, title, description, objectives, status,
    registered_at, submitted_at, approved_at, completed_at, created_by,
    created_at, updated_at, problem_statement, expected_output, milestones_initialized
)
VALUES (
    @TeamId,
    @TargetProjectCode,
    N'Nền tảng Tự động hóa Kiểm thử và Đánh giá Chất lượng Phần mềm Phân tán (Distributed QA Automation Platform)',
    N'Hệ thống quản lý, phân bổ và tự động hóa toàn diện quy trình kiểm thử đơn vị, kiểm thử tích hợp và kiểm thử tải cho các dịch vụ phân tán trên nền tảng Cloud Native.',
    N'Cung cấp quy trình kiểm thử tự động toàn diện, tích hợp CI/CD pipeline, tổng hợp báo cáo kiểm thử chuẩn IEEE, giảm thiểu thời gian kiểm thử hồi quy và nâng cao độ tin cậy của phần mềm trong kiến trúc Microservices.',
    N'ACTIVE',
    '2026-08-21 09:00:00',
    '2026-08-28 15:00:00',
    '2026-09-12 10:00:00',
    NULL,
    @LeaderUserId,
    '2026-08-21 09:00:00',
    '2026-09-15 16:00:00',
    N'Các hệ thống phần mềm phân tán quy mô lớn gặp khó khăn trong việc kiểm thử hồi quy tự động, đo lường độ bao phủ mã nguồn theo thời gian thực và quản lý lỗi phân tán trong chu trình Agile CI/CD.',
    N'Hệ thống Web App quản trị kiểm thử hoàn chỉnh, API điều phối Agent kiểm thử phân tán, bảng điều khiển trực quan hóa độ bao phủ và báo cáo kiểm thử tự động.',
    1
);

DECLARE @ProjectId bigint = SCOPE_IDENTITY();

-- BẢO ĐẢM ĐẶC TRƯNG ĐƠN NGÀNH: CHỈ CÓ DUY NHẤT 1 BẢN GHI TRONG project_majors
INSERT INTO dbo.project_majors (project_id, major_id, created_at)
VALUES (@ProjectId, @MajorSeId, GETUTCDATE());

-- project_major_requirements cho dự án đơn ngành SE
INSERT INTO dbo.project_major_requirements (
    project_id, major_id, min_members, max_members, responsibility, concurrency_token, created_at, updated_at
)
VALUES (
    @ProjectId, @MajorSeId, 3, 5,
    N'Đảm nhiệm toàn bộ vòng đời phát triển phần mềm (SDLC), kiến trúc hệ thống phân tán, backend ASP.NET Core, frontend React, CI/CD pipeline và các kịch bản kiểm thử tự động.',
    NEWID(), GETUTCDATE(), GETUTCDATE()
);

PRINT N'-> Đồ án ID: ' + CAST(@ProjectId AS nvarchar(10)) + N' đã tạo thành công với cấu hình ĐƠN NGÀNH SE.';

-- ==============================================================================================
-- 5. LỊCH SỬ XÉT DUYỆT & QUY TRÌNH PHÊ DUYỆT BỘ MÔN (PROJECT STATUS HISTORY)
-- ==============================================================================================
PRINT N'5. Thiết lập lịch sử xét duyệt đầy đủ các bước (DRAFT -> SUBMITTED -> UNDER_REVIEW -> APPROVED -> SUPERVISOR_PENDING -> ACTIVE)...';

INSERT INTO dbo.project_status_history (project_id, old_status, new_status, changed_by, reason, changed_at)
VALUES
    (@ProjectId, NULL, N'DRAFT', @LeaderUserId, 
     N'Trưởng nhóm Nguyễn Văn Hùng khởi tạo hồ sơ đăng ký đề tài đồ án tốt nghiệp chuyên ngành Kỹ thuật Phần mềm (SE).', 
     '2026-08-21 09:00:00'),
    (@ProjectId, N'DRAFT', N'SUBMITTED', @LeaderUserId, 
     N'Nhóm hoàn thiện đề cương chi tiết đề tài và gửi Bộ môn Kỹ thuật Phần mềm xét duyệt.', 
     '2026-08-28 15:00:00'),
    (@ProjectId, N'SUBMITTED', N'UNDER_REVIEW', @CoordinatorUserId, 
     N'Bộ môn Kỹ thuật Phần mềm tiếp nhận đề tài và phân công hội đồng chuyên môn thẩm định tính khả thi và phạm vi kỹ thuật.', 
     '2026-09-02 10:00:00'),
    (@ProjectId, N'UNDER_REVIEW', N'APPROVED', @CoordinatorUserId, 
     N'Hội đồng Bộ môn Kỹ thuật Phần mềm chính thức phê duyệt đề tài; xác nhận đề tài thỏa mãn 100% chuẩn đầu ra đơn ngành SE.', 
     '2026-09-12 10:00:00'),
    (@ProjectId, N'APPROVED', N'SUPERVISOR_PENDING', @CoordinatorUserId, 
     N'Hệ thống mở cổng kết nối và gửi yêu cầu hướng dẫn đến Giảng viên Hướng dẫn chính theo nguyện vọng của nhóm.', 
     '2026-09-12 10:30:00'),
    (@ProjectId, N'SUPERVISOR_PENDING', N'ACTIVE', @CoordinatorUserId, 
     N'Giảng viên Hướng dẫn chính (ThS. Đỗ Quang Huy) đã ký chấp thuận hướng dẫn; Bộ môn phê duyệt đưa dự án vào giai đoạn thực hiện (ACTIVE).', 
     '2026-09-15 16:00:00');

-- ==============================================================================================
-- 6. QUY TRÌNH KẾT NỐI & PHÂN CÔNG GIẢNG VIÊN HƯỚNG DẪN (SUPERVISOR WORKFLOW)
-- ==============================================================================================
PRINT N'6. Thiết lập yêu cầu hướng dẫn và phân công Giảng viên Hướng dẫn chính (PRIMARY)...';

-- Yêu cầu gửi đến GVHD chính (ThS. Đỗ Quang Huy): assignment_type = 'PRIMARY', major_id = NULL
INSERT INTO dbo.supervisor_requests (
    project_id, supervisor_profile_id, requested_by, assignment_type, major_id, status,
    request_message, response_message, requested_at, expires_at, responded_at, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorProfileId, @LeaderUserId, 'PRIMARY', NULL, N'ACCEPTED',
    N'Kính gửi Thầy ThS. Đỗ Quang Huy, nhóm em gồm 4 sinh viên chuyên ngành Kỹ thuật Phần mềm (SE) kính đề nghị Thầy nhận làm Giảng viên Hướng dẫn chính cho đề tài "Nền tảng Tự động hóa Kiểm thử và Đánh giá Chất lượng Phần mềm Phân tán". Nhóm cam kết tuân thủ nghiêm túc tiến độ và hướng dẫn của Thầy.',
    N'Thầy đồng ý tiếp nhận hướng dẫn chính cho nhóm. Đề tài kiểm thử tự động phân tán có tính ứng dụng thực tiễn cao và khối lượng công việc phù hợp với đồ án Kỹ thuật Phần mềm đơn ngành. Nhóm cần tập trung hoàn thiện kiến trúc hệ thống và quy chuẩn kịch bản kiểm thử trong các Sprint đầu.',
    '2026-09-12 11:00:00', '2026-09-20 23:59:59', '2026-09-14 09:30:00', '2026-09-12 11:00:00', '2026-09-14 09:30:00'
);

DECLARE @SupervisorRequestId bigint = SCOPE_IDENTITY();

-- Phân công chính thức (PRIMARY ASSIGNMENT)
INSERT INTO dbo.supervisor_assignments (
    project_id, supervisor_profile_id, supervisor_request_id, is_primary, assignment_type,
    major_id, assigned_by, assigned_at, ended_at, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorProfileId, @SupervisorRequestId, 1, 'PRIMARY',
    NULL, @CoordinatorUserId, '2026-09-15 16:00:00', NULL, '2026-09-15 16:00:00', '2026-09-15 16:00:00'
);

DECLARE @SupervisorAssignmentId bigint = SCOPE_IDENTITY();

PRINT N'-> Đã phân công GVHD chính: ThS. Đỗ Quang Huy (Assignment ID: ' + CAST(@SupervisorAssignmentId AS nvarchar(10)) + N')';

-- ==============================================================================================
-- 7. THỎA ƯỚC LÀM VIỆC NHÓM (TEAM WORKING AGREEMENT & MEMBER SIGNATURES)
-- ==============================================================================================
PRINT N'7. Thiết lập Thỏa ước làm việc nhóm (Working Agreement) và chữ ký 4 thành viên...';

DECLARE @WorkingAgreementJson nvarchar(max) = N'[
  {
    "title": "1. Nguyên tắc Giao tiếp & Tương tác nhóm SE",
    "body": "- Mọi thành viên cam kết theo dõi kênh chat dự án và Discord ít nhất 3 lần/ngày.\n- Phản hồi tin nhắn và email công việc trong vòng tối đa 3 tiếng vào ban ngày.\n- Giữ thái độ cầu thị, tôn trọng và minh bạch trong mọi vấn đề kỹ thuật."
  },
  {
    "title": "2. Tiêu chuẩn Mã nguồn & Gitflow",
    "body": "- Tuân thủ Clean Architecture và SOLID principles trên ASP.NET Core & TypeScript.\n- Đặt tên branch theo định dạng: feat/SE01-task-id, fix/SE01-task-id, test/SE01-task-id.\n- Mỗi Pull Request bắt buộc có ít nhất 1 thành viên review và approve, đồng thời toàn bộ Unit Test phải pass 100% trước khi merge vào develop."
  },
  {
    "title": "3. Kỷ luật Sprint & Quản lý Blocker",
    "body": "- Tham gia đầy đủ Daily Scrum (15 phút mỗi sáng) và Sprint Review/Retrospective cuối tuần.\n- Khi gặp khó khăn kỹ thuật (Blocker) vượt quá 8 tiếng, bắt buộc phải báo ngay cho Trưởng nhóm hoặc Thầy Hướng dẫn để được tháo gỡ kịp thời."
  },
  {
    "title": "4. Chất lượng & Tự động hóa Kiểm thử (Definition of Done)",
    "body": "- Mọi tính năng mới phải có Unit Test đạt độ bao phủ tối thiểu 80% line coverage.\n- Tích hợp kiểm thử tự động vào CI/CD GitHub Actions trên mọi commit."
  }
]';

INSERT INTO dbo.project_working_agreements (
    project_id, version, status, content_json, concurrency_token, created_by, created_at, published_at
)
VALUES (
    @ProjectId, 1, 'ACTIVE', @WorkingAgreementJson, NEWID(), @LeaderUserId, '2026-09-16 08:00:00', '2026-09-16 10:00:00'
);

DECLARE @WorkingAgreementId bigint = SCOPE_IDENTITY();

INSERT INTO dbo.agreement_members (agreement_id, user_id, team_member_id, membership_joined_at, membership_generation, accepted_at)
VALUES
    (@WorkingAgreementId, @LeaderUserId, @TeamMemberLeaderId, '2026-08-16 09:00:00', @GenLeader, '2026-09-16 10:15:00'),
    (@WorkingAgreementId, @Member2UserId, @TeamMemberM2Id, '2026-08-16 09:15:00', @GenM2, '2026-09-16 10:30:00'),
    (@WorkingAgreementId, @Member3UserId, @TeamMemberM3Id, '2026-08-16 09:20:00', @GenM3, '2026-09-16 11:00:00'),
    (@WorkingAgreementId, @Member4UserId, @TeamMemberM4Id, '2026-08-16 09:30:00', @GenM4, '2026-09-16 11:15:00');

-- ==============================================================================================
-- 8. GIAI ĐOẠN ĐỒ ÁN (MILESTONES - 4 GIAI ĐOẠN THEO TIẾN ĐỘ THỰC TẾ)
-- ==============================================================================================
PRINT N'8. Khởi tạo 4 Giai đoạn đồ án (Milestones)...';

INSERT INTO dbo.milestones (
    project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token
)
VALUES
    (@ProjectId, 
     N'M1 - Phân tích Yêu cầu, Đặc tả Kịch bản Kiểm thử & Thiết kế Cơ sở',
     N'Khảo sát các mô hình kiểm thử phân tán, xác định yêu cầu chức năng/phi chức năng, lập tài liệu Software Requirements Specification (SRS) và thiết kế ERD cơ sở dữ liệu.',
     '2026-09-16', '2026-09-30', N'COMPLETED', 1, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID()),
    
    (@ProjectId, 
     N'M2 - Thiết kế Kiến trúc Hệ thống, API Orchestrator & Worker Agents',
     N'Thiết kế kiến trúc phân tán (Master-Worker), triển khai API Orchestrator trên ASP.NET Core Web API, module xác thực JWT/RBAC và giao thức điều phối kiểm thử.',
     '2026-10-01', '2026-10-20', N'COMPLETED', 2, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID()),
    
    (@ProjectId, 
     N'M3 - Tự động hóa Thực thi Kịch bản Kiểm thử & Trực quan Báo cáo',
     N'Xây dựng Test Runner Agent, tự động phân bổ tải kiểm thử lên các node Worker, thu thập kết quả log/trạng thái và hiển thị bảng điều khiển kết quả kiểm thử theo thời gian thực.',
     '2026-10-21', '2026-11-25', N'IN_PROGRESS', 3, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID()),
    
    (@ProjectId, 
     N'M4 - Tối ưu Hiệu năng, Đánh giá Bảo mật & Bảo vệ Đồ án Tốt nghiệp',
     N'Thực hiện kiểm thử tải hệ thống (Load & Stress Testing), hoàn thiện tài liệu kỹ thuật, tài liệu hướng dẫn triển khai, slide thuyết trình và bảo vệ trước Hội đồng chấm thi.',
     '2026-11-26', '2026-12-31', N'PLANNED', 4, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());

DECLARE @Milestone1Id bigint = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 1);
DECLARE @Milestone2Id bigint = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 2);
DECLARE @Milestone3Id bigint = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 3);
DECLARE @Milestone4Id bigint = (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 4);

-- ==============================================================================================
-- 9. CHU KỲ SPRINT AGILE (SPRINTS - SPRINT 1: XONG, SPRINT 2: ACTIVE, SPRINT 3: KẾ HOẠCH)
-- ==============================================================================================
PRINT N'9. Khởi tạo các chu kỳ Sprint Agile (Sprint 1: COMPLETED, Sprint 2: ACTIVE, Sprint 3: PLANNED)...';

INSERT INTO dbo.sprints (
    project_id, name, status, start_at, end_at, started_at, completed_at, concurrency_token, created_by, created_at, updated_at
)
VALUES
    (@ProjectId, 
     N'Sprint 1 - Thiết kế Database, API Orchestrator & Core Authentication',
     'COMPLETED', '2026-09-16 00:00:00', '2026-10-05 23:59:59', '2026-09-16 08:00:00', '2026-10-05 17:30:00',
     NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    
    (@ProjectId, 
     N'Sprint 2 - Triển khai Worker Agent Kiểm thử & Dashboard Giám sát',
     'ACTIVE', '2026-10-06 00:00:00', '2026-10-31 23:59:59', '2026-10-06 08:30:00', NULL,
     NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    
    (@ProjectId, 
     N'Sprint 3 - Tích hợp CI/CD Pipeline, Đo kiểm Tải & Báo cáo Chuẩn IEEE',
     'PLANNED', '2026-11-01 00:00:00', '2026-11-25 23:59:59', NULL, NULL,
     NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());

DECLARE @Sprint1Id bigint = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 1%');
DECLARE @Sprint2Id bigint = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 2%');
DECLARE @Sprint3Id bigint = (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 3%');

-- ==============================================================================================
-- 10. NHIỆM VỤ KANBAN & PHÂN CÔNG THÀNH VIÊN (TASKS, ASSIGNEES, LABELS, HISTORY)
-- ==============================================================================================
PRINT N'10. Khởi tạo danh sách nhiệm vụ Kanban và phân công cho 4 thành viên SE...';

-- Danh sách nhiệm vụ mô phỏng thực tế cho đề tài kiểm thử tự động phân tán
DECLARE @TaskTable TABLE (
    idx int identity(1,1),
    task_code varchar(20),
    title nvarchar(300),
    description nvarchar(1000),
    status nvarchar(20),
    priority nvarchar(20),
    story_points int,
    milestone_id bigint,
    sprint_id bigint,
    assignee_id bigint,
    label varchar(50),
    start_at datetime2,
    due_at datetime2,
    completed_at datetime2
);

INSERT INTO @TaskTable (task_code, title, description, status, priority, story_points, milestone_id, sprint_id, assignee_id, label, start_at, due_at, completed_at)
VALUES
    -- Các task đã hoàn thành (DONE) thuộc Sprint 1
    ('SE01-01', N'Khảo sát và lập tài liệu đặc tả yêu cầu SRS', 
     N'Phân tích chi tiết quy trình điều phối kiểm thử tự động, lập danh mục Use Cases và đặc tả phi chức năng về hiệu năng và bảo mật.',
     N'DONE', N'HIGH', 5, @Milestone1Id, @Sprint1Id, @LeaderUserId, 'Architecture', '2026-09-16 08:00:00', '2026-09-22 18:00:00', '2026-09-21 17:00:00'),
    
    ('SE01-02', N'Thiết kế mô hình cơ sở dữ liệu và chuẩn hóa quan hệ', 
     N'Thiết kế sơ đồ ERD SQL Server cho Test Suites, Test Cases, Execution Runs, Test Logs và Worker Nodes. Kiểm tra chặt chẽ các ràng buộc toàn vẹn.',
     N'DONE', N'CRITICAL', 8, @Milestone1Id, @Sprint1Id, @Member3UserId, 'Database', '2026-09-20 08:00:00', '2026-09-28 18:00:00', '2026-09-27 16:30:00'),
    
    ('SE01-03', N'Triển khai module xác thực JWT và phân quyền RBAC', 
     N'Cấu hình ASP.NET Core Identity, JWT Bearer authentication, middleware kiểm tra quyền hạn cho Leader, Member và Worker Agent.',
     N'DONE', N'HIGH', 5, @Milestone2Id, @Sprint1Id, @LeaderUserId, 'Backend', '2026-09-28 08:00:00', '2026-10-04 18:00:00', '2026-10-04 15:00:00'),
    
    ('SE01-04', N'Xây dựng UI Layout cơ sở và Theme trực quan React', 
     N'Thiết lập dự án React Vite, Ant Design/Tailwind CSS, cấu hình React Router, Redux Toolkit và giao diện khung quản trị responsive.',
     N'DONE', N'MEDIUM', 3, @Milestone2Id, @Sprint1Id, @Member2UserId, 'Frontend', '2026-09-26 08:00:00', '2026-10-03 18:00:00', '2026-10-03 16:00:00'),

    -- Các task đang làm (IN_PROGRESS) và review (IN_REVIEW) thuộc Sprint 2 (ACTIVE)
    ('SE01-05', N'Lập trình API Orchestrator điều phối hàng đợi kịch bản kiểm thử', 
     N'Xây dựng hàng đợi thông điệp nội bộ (In-memory/Redis Queue), thuật toán phân phối Test Jobs cân bằng tải giữa các node Agent đang rảnh.',
     N'IN_PROGRESS', N'CRITICAL', 8, @Milestone3Id, @Sprint2Id, @LeaderUserId, 'Backend', '2026-10-06 08:00:00', '2026-10-28 18:00:00', NULL),

    ('SE01-06', N'Phát triển Worker Test Agent Runner trên môi trường Docker', 
     N'Đóng gói runner containerized có khả năng kéo test scripts, thực thi headless browser Playwright/Selenium và bắt stdout/stderr.',
     N'IN_PROGRESS', N'HIGH', 5, @Milestone3Id, @Sprint2Id, @Member3UserId, 'DevOps', '2026-10-08 08:00:00', '2026-10-27 18:00:00', NULL),

    ('SE01-07', N'Xây dựng giao diện Dashboard giám sát kết quả Test Run thời gian thực', 
     N'Hiển thị tiến độ kiểm thử dạng thanh trạng thái (Passing, Failing, Error), biểu đồ donut tỉ lệ thành công và bảng danh sách ca kiểm thử chi tiết.',
     N'IN_PROGRESS', N'HIGH', 5, @Milestone3Id, @Sprint2Id, @Member2UserId, 'Frontend', '2026-10-10 08:00:00', '2026-10-29 18:00:00', NULL),

    ('SE01-08', N'Tự động phân tích Test Failure và trích xuất Stack Trace', 
     N'Module phân tích cú pháp log lỗi khi kiểm thử thất bại, tự động gom nhóm lỗi trùng lặp và liên kết đến dòng mã nguồn phát sinh ngoại lệ.',
     N'IN_REVIEW', N'MEDIUM', 3, @Milestone3Id, @Sprint2Id, @Member4UserId, 'Testing', '2026-10-12 08:00:00', '2026-10-26 18:00:00', NULL),

    -- Các task TODO chuẩn bị cho cuối Sprint 2 và Sprint 3
    ('SE01-09', N'Tích hợp tính năng xuất báo cáo kiểm thử tự động định dạng HTML/PDF', 
     N'Sinh báo cáo tổng hợp chất lượng phần mềm theo tiêu chuẩn IEEE 829, kèm bảng phân tích độ tin cậy và sơ đồ thời gian thực thi.',
     N'TODO', N'MEDIUM', 3, @Milestone3Id, @Sprint2Id, @Member4UserId, 'Frontend', '2026-10-25 08:00:00', '2026-10-31 18:00:00', NULL),

    ('SE01-10', N'Thiết lập CI/CD GitHub Actions chạy tự động kiểm thử hồi quy', 
     N'Viết workflow GitHub Actions kích hoạt bộ kịch bản kiểm thử hồi quy tự động mỗi khi có Pull Request vào nhánh develop.',
     N'TODO', N'HIGH', 5, @Milestone4Id, @Sprint3Id, @Member3UserId, 'DevOps', '2026-11-01 08:00:00', '2026-11-10 18:00:00', NULL),

    ('SE01-11', N'Đo kiểm tải hệ thống và tối ưu băng thông phân tán (Stress Testing)', 
     N'Sử dụng k6/JMeter để mô phỏng 100 node worker đồng thời gửi kết quả kiểm thử về API Orchestrator, kiểm tra ngưỡng chịu tải và độ trễ phản hồi.',
     N'TODO', N'MEDIUM', 5, @Milestone4Id, @Sprint3Id, @Member4UserId, 'Testing', '2026-11-10 08:00:00', '2026-11-18 18:00:00', NULL),

    ('SE01-12', N'Hoàn thiện tài liệu nghiệm thu kỹ thuật và slide bảo vệ đồ án', 
     N'Tổng kết báo cáo tốt nghiệp kỹ thuật phần mềm, lập sơ đồ kiến trúc triển khai thực tế và chuẩn bị demo sản phẩm trực quan trước hội đồng.',
     N'TODO', N'HIGH', 3, @Milestone4Id, @Sprint3Id, @LeaderUserId, 'Architecture', '2026-11-18 08:00:00', '2026-11-25 18:00:00', NULL);

-- Con trỏ duyệt bảng và insert vào tasks
DECLARE @t_code varchar(20), @t_title nvarchar(300), @t_desc nvarchar(1000), @t_status nvarchar(20), @t_priority nvarchar(20);
DECLARE @t_sp int, @t_m_id bigint, @t_sp_id bigint, @t_assignee bigint, @t_label varchar(50);
DECLARE @t_start datetime2, @t_due datetime2, @t_comp datetime2;
DECLARE @InsertedTaskId bigint;

DECLARE task_cursor CURSOR LOCAL FORWARD_ONLY READ_ONLY FOR
SELECT task_code, title, description, status, priority, story_points, milestone_id, sprint_id, assignee_id, label, start_at, due_at, completed_at
FROM @TaskTable ORDER BY idx;

OPEN task_cursor;
FETCH NEXT FROM task_cursor INTO @t_code, @t_title, @t_desc, @t_status, @t_priority, @t_sp, @t_m_id, @t_sp_id, @t_assignee, @t_label, @t_start, @t_due, @t_comp;

WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO dbo.tasks (
        milestone_id, parent_task_id, title, description, status, priority,
        start_at, due_at, completed_at, created_by, created_at, updated_at,
        concurrency_token, sprint_id, story_points
    )
    VALUES (
        @t_m_id, NULL, @t_title, @t_desc, @t_status, @t_priority,
        @t_start, @t_due, @t_comp, @LeaderUserId, @t_start, ISNULL(@t_comp, GETUTCDATE()),
        NEWID(), @t_sp_id, @t_sp
    );
    SET @InsertedTaskId = SCOPE_IDENTITY();

    -- Phân công người thực hiện
    IF @t_assignee IS NOT NULL
    BEGIN
        INSERT INTO dbo.task_assignees (task_id, user_id, assigned_by, assigned_at)
        VALUES (@InsertedTaskId, @t_assignee, @LeaderUserId, @t_start);
    END

    -- Gán nhãn bảng Kanban
    IF @t_label IS NOT NULL
    BEGIN
        INSERT INTO dbo.task_board_labels (task_id, label)
        VALUES (@InsertedTaskId, @t_label);
    END

    -- Ghi nhận lịch sử trạng thái nhiệm vụ
    IF @t_status = N'DONE'
    BEGIN
        INSERT INTO dbo.task_status_history (task_id, old_status, new_status, changed_by, reason, changed_at)
        VALUES
            (@InsertedTaskId, NULL, N'TODO', @LeaderUserId, N'Tạo nhiệm vụ ban đầu trong Sprint.', @t_start),
            (@InsertedTaskId, N'TODO', N'IN_PROGRESS', @t_assignee, N'Thành viên nhận việc và bắt đầu thực hiện.', DATEADD(hour, 4, @t_start)),
            (@InsertedTaskId, N'IN_PROGRESS', N'DONE', @t_assignee, N'Hoàn thành lập trình, kiểm thử và vượt qua review.', @t_comp);
    END
    ELSE IF @t_status = N'IN_PROGRESS'
    BEGIN
        INSERT INTO dbo.task_status_history (task_id, old_status, new_status, changed_by, reason, changed_at)
        VALUES
            (@InsertedTaskId, NULL, N'TODO', @LeaderUserId, N'Tạo nhiệm vụ ban đầu trong Sprint.', @t_start),
            (@InsertedTaskId, N'TODO', N'IN_PROGRESS', @t_assignee, N'Thành viên đang tích cực triển khai mã nguồn.', DATEADD(hour, 2, @t_start));
    END
    ELSE IF @t_status = N'IN_REVIEW'
    BEGIN
        INSERT INTO dbo.task_status_history (task_id, old_status, new_status, changed_by, reason, changed_at)
        VALUES
            (@InsertedTaskId, NULL, N'TODO', @LeaderUserId, N'Tạo nhiệm vụ ban đầu.', @t_start),
            (@InsertedTaskId, N'TODO', N'IN_PROGRESS', @t_assignee, N'Bắt đầu code.', DATEADD(hour, 2, @t_start)),
            (@InsertedTaskId, N'IN_PROGRESS', N'IN_REVIEW', @t_assignee, N'Đã mở Pull Request, chờ đồng đội review mã nguồn.', DATEADD(day, 2, @t_start));
    END
    ELSE
    BEGIN
        INSERT INTO dbo.task_status_history (task_id, old_status, new_status, changed_by, reason, changed_at)
        VALUES
            (@InsertedTaskId, NULL, N'TODO', @LeaderUserId, N'Nhiệm vụ đã lên kế hoạch trong backlog.', @t_start);
    END

    FETCH NEXT FROM task_cursor INTO @t_code, @t_title, @t_desc, @t_status, @t_priority, @t_sp, @t_m_id, @t_sp_id, @t_assignee, @t_label, @t_start, @t_due, @t_comp;
END

CLOSE task_cursor;
DEALLOCATE task_cursor;

PRINT N'-> Đã khởi tạo thành công 12 nhiệm vụ Kanban, Story Points và Assignees.';

-- ==============================================================================================
-- 11. CUỘC HỌP & BIÊN BẢN HỌP (MEETINGS, ATTENDEES, DECISIONS, ACTION ITEMS)
-- ==============================================================================================
PRINT N'11. Khởi tạo lịch họp nhóm và các buổi họp hướng dẫn với GVHD Đỗ Quang Huy...';

-- Họp 1: Họp khởi động và thống nhất phương án kiến trúc với GVHD (COMPLETED)
INSERT INTO dbo.meetings (
    project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url,
    status, created_by, created_at, updated_at, meeting_delivery_mode, video_channel,
    concurrency_token, recurrence, notes_version
)
VALUES (
    @ProjectId,
    N'Họp khởi động đồ án & Thống nhất phương án kiến trúc với GVHD',
    N'1. Trình bày phạm vi đề tài kiểm thử tự động phân tán.\n2. Lắng nghe nhận xét và định hướng công nghệ của Thầy Đỗ Quang Huy.\n3. Thống nhất kế hoạch Sprint 1 và phân bổ công việc.',
    N'Thầy Huy lưu ý cần chú trọng tính chịu lỗi (fault-tolerance) khi Worker Agent bị rớt kết nối. Cơ sở dữ liệu cần lưu log dạng streaming hoặc batching để tránh quá tải I/O.',
    '2026-09-18 14:00:00', '2026-09-18 15:30:00', N'Phòng Lab Kỹ thuật Phần mềm (B204)', N'https://meet.google.com/aipms-se01-kickoff',
    N'COMPLETED', @LeaderUserId, '2026-09-16 14:00:00', '2026-09-18 16:00:00', 'HYBRID', 'EXTERNAL_LINK',
    NEWID(), 'NONE', 0
);
DECLARE @Meeting1Id bigint = SCOPE_IDENTITY();

-- Quyết định cuộc họp 1
INSERT INTO dbo.meeting_decisions (meeting_id, content, decided_by, decided_at, created_at)
VALUES
    (@Meeting1Id, N'Thống nhất sử dụng kiến trúc Master-Worker với ASP.NET Core Web API làm Orchestrator trung tâm.', @SupervisorUserId, '2026-09-18 15:00:00', GETUTCDATE()),
    (@Meeting1Id, N'Chọn Playwright làm engine chạy kiểm thử tự động mặc định trên Worker Nodes.', @LeaderUserId, '2026-09-18 15:15:00', GETUTCDATE());

-- Việc cần làm sau họp 1
INSERT INTO dbo.meeting_action_items (meeting_id, title, description, assignee_user_id, due_at, status, concurrency_token, created_by, created_at, updated_at)
VALUES
    (@Meeting1Id, N'Hoàn thiện sơ đồ sequence diagram luồng điều phối', N'Vẽ chi tiết luồng heartbeat và job dispatching giữa Orchestrator và Agent.', @LeaderUserId, '2026-09-24 18:00:00', N'DONE', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    (@Meeting1Id, N'Khởi tạo repo GitHub và cấu hình ruleset branch', N'Tạo repo, set branch protection cho main/develop và mời 4 thành viên.', @Member3UserId, '2026-09-20 18:00:00', N'DONE', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());

-- Họp 2: Review kết quả Sprint 1 & Thống nhất giao thức Worker Agent (COMPLETED)
INSERT INTO dbo.meetings (
    project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url,
    status, created_by, created_at, updated_at, meeting_delivery_mode, video_channel,
    concurrency_token, recurrence, notes_version
)
VALUES (
    @ProjectId,
    N'Review kết quả Sprint 1 & Thống nhất giao thức Worker Agent',
    N'1. Demo hệ thống API Orchestrator và mô hình CSDL hoàn chỉnh.\n2. Thảo luận cơ chế xác thực Token định danh dành riêng cho Worker Nodes.\n3. Đánh giá tiến độ Sprint 1 và lập kế hoạch Sprint 2.',
    N'Sprint 1 hoàn thành 100% mục tiêu. Thầy Huy đánh giá cao thiết kế CSDL phân tán có lưu đầy đủ audit trail và chỉ số đo kiểm thời gian.',
    '2026-10-06 09:00:00', '2026-10-06 10:30:00', N'Phòng Họp Trực tuyến FPTU', N'https://meet.google.com/aipms-se01-sprint1',
    N'COMPLETED', @LeaderUserId, '2026-10-04 10:00:00', '2026-10-06 11:00:00', 'REMOTE', 'EXTERNAL_LINK',
    NEWID(), 'NONE', 0
);
DECLARE @Meeting2Id bigint = SCOPE_IDENTITY();

-- Quyết định cuộc họp 2
INSERT INTO dbo.meeting_decisions (meeting_id, content, decided_by, decided_at, created_at)
VALUES
    (@Meeting2Id, N'Chấp thuận kết quả bàn giao Sprint 1, nhóm được chuyển sang Sprint 2.', @SupervisorUserId, '2026-10-06 10:15:00', GETUTCDATE());

-- Họp 3: Họp tiến độ Sprint 2: Đánh giá độ ổn định của Test Runner (COMPLETED)
INSERT INTO dbo.meetings (
    project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url,
    status, created_by, created_at, updated_at, meeting_delivery_mode, video_channel,
    concurrency_token, recurrence, notes_version
)
VALUES (
    @ProjectId,
    N'Họp tiến độ Sprint 2: Đánh giá độ ổn định của Test Runner',
    N'1. Kiểm tra khả năng tự phục hồi của Worker Node khi container bị restart đột ngột.\n2. Xem xét giao diện Dashboard giám sát kết quả kiểm thử trên React.',
    N'Worker Node đã tự reconnect thành công và claim lại job dở dang. Cần bổ sung animation trạng thái running trên giao diện UI để tăng tính trực quan.',
    '2026-10-20 15:00:00', '2026-10-20 16:15:00', N'Phòng Lab Kỹ thuật Phần mềm (B204)', N'https://meet.google.com/aipms-se01-sprint2',
    N'COMPLETED', @LeaderUserId, '2026-10-18 10:00:00', '2026-10-20 16:30:00', 'HYBRID', 'EXTERNAL_LINK',
    NEWID(), 'NONE', 0
);
DECLARE @Meeting3Id bigint = SCOPE_IDENTITY();

-- Họp 4: Họp chuẩn bị Review Gate 2 & Báo cáo tiến độ chu kỳ (SCHEDULED)
INSERT INTO dbo.meetings (
    project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url,
    status, created_by, created_at, updated_at, meeting_delivery_mode, video_channel,
    concurrency_token, recurrence, notes_version
)
VALUES (
    @ProjectId,
    N'Họp chuẩn bị Review Gate 2 & Báo cáo tiến độ chu kỳ',
    N'1. Rà soát danh mục checklist bàn giao cho Review Gate 2.\n2. Tổng duyệt bản demo kiểm thử tự động phân tán end-to-end.\n3. Thống nhất kế hoạch nộp báo cáo tiến độ tuần tiếp theo.',
    NULL,
    '2026-10-29 14:00:00', '2026-10-29 15:30:00', N'Phòng Họp Trực tuyến FPTU', N'https://meet.google.com/aipms-se01-gate2',
    N'SCHEDULED', @LeaderUserId, '2026-10-24 10:00:00', '2026-10-24 10:00:00', 'REMOTE', 'EXTERNAL_LINK',
    NEWID(), 'NONE', 0
);
DECLARE @Meeting4Id bigint = SCOPE_IDENTITY();

-- Gán người tham dự cho các cuộc họp (Meeting Participants)
DECLARE @m_id_iter bigint;
DECLARE meet_cursor CURSOR LOCAL FORWARD_ONLY READ_ONLY FOR SELECT id FROM dbo.meetings WHERE project_id = @ProjectId;
OPEN meet_cursor;
FETCH NEXT FROM meet_cursor INTO @m_id_iter;
WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @att_stat nvarchar(20) = CASE WHEN @m_id_iter = @Meeting4Id THEN NULL ELSE N'ATTENDED' END;
    
    INSERT INTO dbo.meeting_participants (meeting_id, user_id, attendance_status, created_at, updated_at, rsvp_status, rsvp_token)
    VALUES
        (@m_id_iter, @LeaderUserId, @att_stat, GETUTCDATE(), GETUTCDATE(), 'ACCEPTED', NEWID()),
        (@m_id_iter, @Member2UserId, @att_stat, GETUTCDATE(), GETUTCDATE(), 'ACCEPTED', NEWID()),
        (@m_id_iter, @Member3UserId, @att_stat, GETUTCDATE(), GETUTCDATE(), 'ACCEPTED', NEWID()),
        (@m_id_iter, @Member4UserId, @att_stat, GETUTCDATE(), GETUTCDATE(), 'ACCEPTED', NEWID()),
        (@m_id_iter, @SupervisorUserId, @att_stat, GETUTCDATE(), GETUTCDATE(), 'ACCEPTED', NEWID());

    FETCH NEXT FROM meet_cursor INTO @m_id_iter;
END
CLOSE meet_cursor;
DEALLOCATE meet_cursor;

-- ==============================================================================================
-- 12. BÁO CÁO TIẾN ĐỘ & NHẬN XÉT CỦA GIẢNG VIÊN HƯỚNG DẪN (PROGRESS REPORTS & FEEDBACK)
-- ==============================================================================================
PRINT N'12. Khởi tạo các chu kỳ báo cáo tiến độ và nhận xét của GVHD ThS. Đỗ Quang Huy...';

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
    N'Báo cáo tuần 2: Nhóm hoàn thành khảo sát yêu cầu hệ thống và lập đề cương chi tiết SRS.',
    N'- Hoàn thành tài liệu SRS 35 trang mô tả kiến trúc phân tán.\n- Thống nhất mô hình ERD cơ sở dữ liệu với GVHD.\n- Khởi tạo repository Git và phân công nhiệm vụ Sprint 1.',
    N'- Triển khai code các entity EF Core và DbContext.\n- Viết các API CRUD cơ bản cho kịch bản kiểm thử.',
    N'Không có rủi ro lớn. Nhóm đang bám sát tiến độ đã cam kết.',
    N'REVIEWED', '2026-09-23 20:00:00', '2026-09-23 20:00:00', '2026-09-24 09:00:00', NEWID(), @Period1Id,
    0, N'Đang cấu hình project base ASP.NET Core', NULL, NULL, N'Bắt đầu code EF Core DbContext'
);
DECLARE @Report1Id bigint = SCOPE_IDENTITY();

-- Nhận xét của GVHD cho Báo cáo 1
INSERT INTO dbo.supervisor_feedback (
    project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, gate_code,
    feedback_text, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorAssignmentId, @Report1Id, NULL, NULL, NULL,
    N'Nhóm hoàn thành tốt đặc tả yêu cầu và sơ đồ kiến trúc ban đầu. Tài liệu SRS rõ ràng, đúng chuẩn IEEE. Cần hoàn thiện sớm interface giao tiếp của Worker Agent để các bạn làm frontend có thể mock data.',
    '2026-09-24 09:30:00', '2026-09-24 09:30:00'
);

-- Chu kỳ 2: Báo cáo tháng 9 (MONTHLY) - 01/09 đến 30/09
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
    N'Tổng kết tiến độ tháng 9: Hoàn thành 100% mục tiêu Milestone 1 và sẵn sàng bàn giao Sprint 1.',
    N'- Cơ sở dữ liệu đã deploy lên staging SQL Server thành công.\n- Module xác thực JWT và bảo mật API hoàn tất.\n- UI Dashboard bản alpha đã kết nối được API đăng nhập và danh sách dự án.',
    N'- Hoàn thiện cơ chế điều phối Worker Agent trong Sprint 2.\n- Tích hợp headless browser Playwright vào runner container.',
    N'Hiệu năng khi 50 container runner gửi log cùng lúc có thể gây nghẽn hàng đợi; nhóm sẽ thử nghiệm batching.',
    N'REVIEWED', '2026-10-01 19:30:00', '2026-10-01 19:30:00', '2026-10-02 08:30:00', NEWID(), @Period2Id,
    0, N'Đang hoàn thiện các API điều phối hàng đợi', NULL, NULL, N'Triển khai Docker runner'
);
DECLARE @Report2Id bigint = SCOPE_IDENTITY();

-- Nhận xét của GVHD cho Báo cáo 2
INSERT INTO dbo.supervisor_feedback (
    project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, gate_code,
    feedback_text, created_at, updated_at
)
VALUES (
    @ProjectId, @SupervisorAssignmentId, @Report2Id, NULL, NULL, NULL,
    N'Tiến độ tháng 9 đạt 100% kế hoạch. Nhóm làm việc chủ động, kỹ năng git và code review rất bài bản. Thầy đồng ý cho phép nhóm chính thức kích hoạt Sprint 2 theo lịch trình.',
    '2026-10-02 09:00:00', '2026-10-02 09:00:00'
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
    N'Báo cáo tuần 6: Tiến độ Sprint 2 đạt 75% khối lượng công việc, Worker Agent đã chạy ổn định.',
    N'- Đã đóng gói thành công Worker Test Agent chạy độc lập trong Docker.\n- API Orchestrator đã giao việc và nhận kết quả tự động từ 5 nodes song song.\n- Giao diện Dashboard hiển thị thời gian thực tiến độ các ca kiểm thử.',
    N'- Hoàn thành module phân tích log lỗi tự động.\n- Chuẩn bị tài liệu và bằng chứng cho đợt kiểm tra Review Gate 2.',
    N'Không có blocker nghiêm trọng. Một số test case timeout khi mạng chậm, nhóm đang thêm cấu hình retry.',
    N'SUBMITTED', '2026-10-22 17:00:00', '2026-10-22 17:00:00', '2026-10-22 17:00:00', NEWID(), @Period3Id,
    0, N'Tối ưu hóa khả năng bắt log chi tiết', NULL, NULL, N'Chuẩn bị checklist Review Gate 2'
);

-- ==============================================================================================
-- 13. SẢN PHẨM BÀN GIAO & PHIÊN BẢN (DELIVERABLES, VERSIONS & MOCK FILES)
-- ==============================================================================================
PRINT N'13. Khởi tạo sản phẩm bàn giao (Deliverables) và các phiên bản tài liệu/mã nguồn...';

-- Sản phẩm 1: SRS & Kịch bản kiểm thử (ACCEPTED)
INSERT INTO dbo.deliverables (
    project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @Milestone1Id, 
    N'Đặc tả Yêu cầu Phần mềm (SRS) & Danh mục Kịch bản Kiểm thử',
    N'Tài liệu đặc tả toàn diện yêu cầu chức năng, phi chức năng, ca sử dụng và ma trận phân tích truy xuất nguồn gốc (Traceability Matrix).',
    N'DOCUMENT', '2026-09-30 18:00:00', N'ACCEPTED', @LeaderUserId, '2026-09-16 09:00:00', '2026-09-30 15:00:00'
);
DECLARE @Deliv1Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
VALUES 
    (@Deliv1Id, 1, @LeaderUserId, N'Bản thảo SRS v1.0 nộp xin ý kiến GVHD', N'SUPERSEDED', '2026-09-22 16:00:00', '2026-09-22 16:00:00', '2026-09-25 09:00:00'),
    (@Deliv1Id, 2, @LeaderUserId, N'Bản hoàn thiện SRS v2.0 đã tiếp thu và chỉnh sửa theo góp ý của Thầy Huy', N'ACCEPTED', '2026-09-29 14:00:00', '2026-09-29 14:00:00', '2026-09-30 15:00:00');
DECLARE @Deliv1_v2 bigint = SCOPE_IDENTITY();

-- Sản phẩm 2: Tài liệu thiết kế kiến trúc hệ thống & CSDL (ACCEPTED)
INSERT INTO dbo.deliverables (
    project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @Milestone2Id, 
    N'Tài liệu Thiết kế Kiến trúc Hệ thống phân tán, CSDL & API Specification',
    N'Tài liệu kiến trúc phần mềm Clean Architecture, sơ đồ ERD chi tiết, OpenAPI/Swagger specification và mô hình triển khai Docker/Kubernetes.',
    N'DOCUMENT', '2026-10-20 18:00:00', N'ACCEPTED', @LeaderUserId, '2026-10-01 09:00:00', '2026-10-18 16:00:00'
);
DECLARE @Deliv2Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
VALUES 
    (@Deliv2Id, 1, @LeaderUserId, N'Gói tài liệu thiết kế kiến trúc hoàn thiện và script migration CSDL v1.0', N'ACCEPTED', '2026-10-18 14:00:00', '2026-10-18 14:00:00', '2026-10-19 10:00:00');
DECLARE @Deliv2_v1 bigint = SCOPE_IDENTITY();

-- Sản phẩm 3: Gói mã nguồn Core Orchestrator & Worker Agents v1.0 (SUBMITTED)
INSERT INTO dbo.deliverables (
    project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @Milestone3Id, 
    N'Gói mã nguồn Core Orchestrator & Worker Agents v1.0',
    N'Mã nguồn hoàn chỉnh backend ASP.NET Core, Worker Agent Docker, frontend React và bộ unit tests tự động.',
    N'SOURCE_CODE', '2026-11-20 18:00:00', N'SUBMITTED', @LeaderUserId, '2026-10-15 09:00:00', '2026-10-22 16:00:00'
);
DECLARE @Deliv3Id bigint = SCOPE_IDENTITY();

INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
VALUES 
    (@Deliv3Id, 1, @LeaderUserId, N'Phiên bản Release Candidate 1 phục vụ đánh giá Review Gate 2', N'SUBMITTED', '2026-10-22 15:30:00', '2026-10-22 15:30:00', '2026-10-22 15:30:00');
DECLARE @Deliv3_v1 bigint = SCOPE_IDENTITY();

-- Sản phẩm 4: Báo cáo tổng kết đồ án tốt nghiệp & Slide bảo vệ (OPEN)
INSERT INTO dbo.deliverables (
    project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at
)
VALUES (
    @ProjectId, @Milestone4Id, 
    N'Báo cáo Tổng kết Đồ án Tốt nghiệp & Slide Thuyết trình Bảo vệ',
    N'Báo cáo kỹ thuật cuối khóa chuẩn IEEE, video demo kịch bản kiểm thử phân tán và slide thuyết trình trước hội đồng tốt nghiệp.',
    N'DOCUMENT', '2026-12-25 18:00:00', N'OPEN', @LeaderUserId, '2026-11-01 09:00:00', '2026-11-01 09:00:00'
);

-- Bổ sung metadata file đính kèm mô phỏng cho UI
INSERT INTO dbo.files (
    uploaded_by, deliverable_version_id, original_file_name, stored_file_name, storage_path,
    file_url, mime_type, file_size_bytes, checksum_sha256, created_at, updated_at
)
VALUES
    (@LeaderUserId, @Deliv1_v2, N'SEP490_SE01_Software_Requirements_Specification_v2.0.pdf',
     N'sep490_se01_srs_v2_9a1f.pdf', N'/uploads/deliverables/sep490_se01_srs_v2_9a1f.pdf',
     N'https://storage.aipms.fe.edu.vn/deliverables/sep490_se01_srs_v2_9a1f.pdf',
     N'application/pdf', 4125890, N'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', GETUTCDATE(), GETUTCDATE()),
    
    (@LeaderUserId, @Deliv2_v1, N'SEP490_SE01_Architecture_and_Database_Design.zip',
     N'sep490_se01_arch_e8b4.zip', N'/uploads/deliverables/sep490_se01_arch_e8b4.zip',
     N'https://storage.aipms.fe.edu.vn/deliverables/sep490_se01_arch_e8b4.zip',
     N'application/zip', 9450120, N'bca0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b899', GETUTCDATE(), GETUTCDATE());

-- ==============================================================================================
-- 14. THẺ TỪ KHÓA ĐỒ ÁN (TAGS & PROJECT TAGS)
-- ==============================================================================================
PRINT N'14. Gán thẻ phân loại công nghệ và chuyên môn đồ án (Tags)...';

-- Đảm bảo các tag tồn tại
DECLARE @TagDomSe bigint, @TagDomQa bigint;
DECLARE @TagTechDotnet bigint, @TagTechDocker bigint, @TagTechReact bigint, @TagTechPostgres bigint;
DECLARE @TagKwSingleMajor bigint, @TagKwQaAuto bigint;

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'SOFTWARE_ENGINEERING')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'Software Engineering', N'SOFTWARE_ENGINEERING', N'DOMAIN', GETUTCDATE());
SET @TagDomSe = (SELECT id FROM dbo.tags WHERE normalized_name = N'SOFTWARE_ENGINEERING');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'QA_AND_TESTING')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'Quality Assurance & Testing', N'QA_AND_TESTING', N'DOMAIN', GETUTCDATE());
SET @TagDomQa = (SELECT id FROM dbo.tags WHERE normalized_name = N'QA_AND_TESTING');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'NET_8')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'.NET 8 / ASP.NET Core', N'NET_8', N'TECHNOLOGY', GETUTCDATE());
SET @TagTechDotnet = (SELECT id FROM dbo.tags WHERE normalized_name = N'NET_8');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'DOCKER')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'Docker Containers', N'DOCKER', N'TECHNOLOGY', GETUTCDATE());
SET @TagTechDocker = (SELECT id FROM dbo.tags WHERE normalized_name = N'DOCKER');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'REACT')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'React', N'REACT', N'TECHNOLOGY', GETUTCDATE());
SET @TagTechReact = (SELECT id FROM dbo.tags WHERE normalized_name = N'REACT');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'SINGLE_MAJOR_SE')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'SingleMajor_SE', N'SINGLE_MAJOR_SE', N'KEYWORD', GETUTCDATE());
SET @TagKwSingleMajor = (SELECT id FROM dbo.tags WHERE normalized_name = N'SINGLE_MAJOR_SE');

IF NOT EXISTS (SELECT 1 FROM dbo.tags WHERE normalized_name = N'DISTRIBUTED_TESTING')
    INSERT INTO dbo.tags (name, normalized_name, tag_type, created_at) VALUES (N'DistributedTesting', N'DISTRIBUTED_TESTING', N'KEYWORD', GETUTCDATE());
SET @TagKwQaAuto = (SELECT id FROM dbo.tags WHERE normalized_name = N'DISTRIBUTED_TESTING');

INSERT INTO dbo.project_tags (project_id, tag_id)
VALUES
    (@ProjectId, @TagDomSe),
    (@ProjectId, @TagDomQa),
    (@ProjectId, @TagTechDotnet),
    (@ProjectId, @TagTechDocker),
    (@ProjectId, @TagTechReact),
    (@ProjectId, @TagKwSingleMajor),
    (@ProjectId, @TagKwQaAuto);

COMMIT TRANSACTION;

PRINT N'======================================================================================';
PRINT N'ĐÃ HOÀN TẤT SEED DỮ LIỆU ĐỒ ÁN ĐƠN NGÀNH SE THÀNH CÔNG!';
PRINT N'MÃ DỰ ÁN: ' + @TargetProjectCode + N' (ID: ' + CAST(@ProjectId as nvarchar(10)) + N')';
PRINT N'MÃ NHÓM:  ' + @TargetTeamCode + N' (ID: ' + CAST(@TeamId as nvarchar(10)) + N')';
PRINT N'GVHD:     ThS. Đỗ Quang Huy (lecturer.se02@fe.edu.vn - Profile ID: ' + CAST(@SupervisorProfileId as nvarchar(10)) + N')';
PRINT N'LEADER:   Nguyễn Văn Hùng (hungnvse180201@fpt.edu.vn - SE180201)';
PRINT N'======================================================================================';
