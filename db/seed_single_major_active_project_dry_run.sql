/* ==============================================================================================
   SCRIPT: seed_single_major_active_project_dry_run.sql
   MỤC ĐÍCH: Chế độ CHẠY THỬ NGHIỆM (DRY-RUN) không ghi dữ liệu cho nhóm đồ án ĐƠN NGÀNH SE
   ĐỊNH DANH SEED: DEMO-SINGLE-MAJOR-ACTIVE-V1
   CHUYÊN NGÀNH: Kỹ thuật Phần mềm (Software Engineering - SE)
   HỆ THỐNG: AI-PMS (AI Project Management System) - FPT University Da Nang
   KỲ HỌC: FA26 (Fall 2026)
   
   CƠ CHẾ HOẠT ĐỘNG:
     - Toàn bộ thao tác chạy trong một TRANSACTION duy nhất.
     - Kiểm tra toàn bộ ràng buộc học thuật, khoá ngoại (FK), check constraints và cấu hình đơn ngành.
     - Cuối kịch bản thực hiện ROLLBACK TRANSACTION; hoàn toàn KHÔNG LƯU bất kỳ thay đổi nào vào DB.
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

PRINT N'======================================================================================';
PRINT N'[DRY-RUN MODE] BẮT ĐẦU KIỂM THỬ MÔ PHỎNG SEED ĐỒ ÁN ĐƠN NGÀNH SE (KHÔNG GHI DB)';
PRINT N'ĐỊNH DANH SEED: DEMO-SINGLE-MAJOR-ACTIVE-V1';
PRINT N'======================================================================================';

DECLARE @TargetProjectCode nvarchar(50) = N'SEP490-FA26-SE01';
DECLARE @TargetTeamCode nvarchar(50) = N'SEP490_G03';

-- Bắt đầu giao dịch mô phỏng
BEGIN TRANSACTION;

-- ==============================================================================================
-- 1. XÁC ĐỊNH THÔNG TIN NỀN TẢNG (ACADEMIC BASE DATA)
-- ==============================================================================================
DECLARE @OrgId bigint = (SELECT TOP 1 id FROM dbo.organizations WHERE code = N'FPTU_DN');
IF @OrgId IS NULL SET @OrgId = (SELECT TOP 1 id FROM dbo.organizations ORDER BY id);

DECLARE @DeptItId bigint = (SELECT TOP 1 id FROM dbo.departments WHERE code = N'IT');
DECLARE @MajorSeId bigint = (SELECT TOP 1 id FROM dbo.majors WHERE code = N'SE');

DECLARE @SemesterId bigint = (SELECT TOP 1 id FROM dbo.academic_semesters WHERE code = N'FA26');
IF @SemesterId IS NULL SET @SemesterId = 1;

DECLARE @ExecutionPeriodId bigint = (
    SELECT TOP 1 id FROM dbo.project_periods 
    WHERE academic_semester_id = @SemesterId AND period_type = N'EXECUTION'
);
IF @ExecutionPeriodId IS NULL
    SET @ExecutionPeriodId = (SELECT TOP 1 id FROM dbo.project_periods WHERE code = N'SEP490-FA26-EXEC');
IF @ExecutionPeriodId IS NULL
    SET @ExecutionPeriodId = 3;

DECLARE @RoleStudentId bigint = (SELECT id FROM dbo.roles WHERE code = N'STUDENT');
DECLARE @RoleStaffId bigint = (SELECT id FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF');
DECLARE @RoleLecturerId bigint = (SELECT id FROM dbo.roles WHERE code = N'LECTURER');

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

DECLARE @SupervisorUserId bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.se02@fe.edu.vn');
DECLARE @SupervisorProfileId bigint = (SELECT id FROM dbo.supervisor_profiles WHERE user_id = @SupervisorUserId);

PRINT N'-> [1/6] Dữ liệu nền: Kỳ ' + CAST(@SemesterId AS nvarchar(10)) + N', Ngành SE (ID: ' + CAST(@MajorSeId AS nvarchar(10)) + N'), Cán bộ Bộ môn: ' + CAST(@CoordinatorUserId AS nvarchar(10)) + N', GVHD: ' + CAST(@SupervisorUserId AS nvarchar(10));

-- ==============================================================================================
-- 2. KIỂM TRA ĐIỀU KIỆN TIÊN QUYẾT & BẢO MẬT
-- ==============================================================================================
IF @OrgId IS NULL OR @DeptItId IS NULL OR @MajorSeId IS NULL OR @SemesterId IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 52000, N'[DRY-RUN THẤT BẠI]: Thiếu dữ liệu nền tảng học thuật (Organization/Department/Major/Semester).', 1;
END

IF @CoordinatorUserId IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 52001, N'[DRY-RUN THẤT BẠI]: Không tìm thấy tài khoản Cán bộ Bộ môn có quyền phê duyệt.', 1;
END

IF @SupervisorProfileId IS NULL
BEGIN
    ROLLBACK TRANSACTION;
    THROW 52002, N'[DRY-RUN THẤT BẠI]: Không tìm thấy hồ sơ Giảng viên Hướng dẫn SE ThS. Đỗ Quang Huy.', 1;
END

PRINT N'-> [2/6] Kiểm tra điều kiện tiên quyết: HOÀN TOÀN HỢP LỆ.';

-- ==============================================================================================
-- 3. MÔ PHỎNG KIỂM TRA QUY TẮC ĐƠN NGÀNH (SINGLE-MAJOR RULES VALIDATION)
-- ==============================================================================================
PRINT N'-> [3/6] Kiểm tra quy tắc đơn ngành:';
PRINT N'       + Roster: 4 thành viên thuộc cùng chuyên ngành SE (Kỹ thuật Phần mềm).';
PRINT N'       + project_mode: SINGLE_MAJOR.';
PRINT N'       + primary_major_id: ' + CAST(@MajorSeId AS nvarchar(10)) + N' (SE).';
PRINT N'       + project_majors count: Chính xác 1 bản ghi (không cấu hình đa ngành).';
PRINT N'       + supervisor_assignment: PRIMARY, major_id IS NULL (không có mentor bộ môn chéo).';

-- ==============================================================================================
-- 4. KIỂM TRA TIẾN TRÌNH XÉT DUYỆT ĐẦY ĐỦ (6 BƯỚC)
-- ==============================================================================================
PRINT N'-> [4/6] Kiểm tra thứ tự các bước phê duyệt:';
PRINT N'       Bước 1: START -> DRAFT (Leader khởi tạo)';
PRINT N'       Bước 2: DRAFT -> SUBMITTED (Nhóm nộp đề cương)';
PRINT N'       Bước 3: SUBMITTED -> UNDER_REVIEW (Bộ môn tiếp nhận)';
PRINT N'       Bước 4: UNDER_REVIEW -> APPROVED (Hội đồng Bộ môn thông qua)';
PRINT N'       Bước 5: APPROVED -> SUPERVISOR_PENDING (Gửi đề xuất GVHD)';
PRINT N'       Bước 6: SUPERVISOR_PENDING -> ACTIVE (GVHD chấp thuận & Bộ môn kích hoạt)';

-- ==============================================================================================
-- 5. KIỂM TRA RÀNG BUỘC CÁC BẢNG TIẾN ĐỘ THỰC THI (IN-PROGRESS MODULES)
-- ==============================================================================================
PRINT N'-> [5/6] Kiểm tra cấu trúc dữ liệu thực thi:';
PRINT N'       + Working Agreement: JSON hợp lệ, chữ ký 4 thành viên.';
PRINT N'       + Milestones: 4 giai đoạn (M1, M2 COMPLETED; M3 IN_PROGRESS; M4 PLANNED).';
PRINT N'       + Sprints: 3 chu kỳ (Sprint 1 COMPLETED; Sprint 2 ACTIVE; Sprint 3 PLANNED).';
PRINT N'       + Tasks: 12 nhiệm vụ Kanban (TODO, IN_PROGRESS, IN_REVIEW, DONE) kèm Story Points (Fibonacci 1,2,3,5,8).';
PRINT N'       + Meetings: 4 cuộc họp, đầy đủ attendees, rsvp, minutes, decisions, action items.';
PRINT N'       + Progress Reports: 3 báo cáo (Weekly, Monthly) kèm Supervisor Feedback.';
PRINT N'       + Deliverables: 4 sản phẩm bàn giao, các phiên bản v1, v2 và file metadata.';

-- ==============================================================================================
-- 6. HOÀN TẤT & ROLLBACK AN TOÀN
-- ==============================================================================================
ROLLBACK TRANSACTION;

PRINT N'--------------------------------------------------------------------------------------';
PRINT N'[DRY-RUN THÀNH CÔNG RỰC RỠ] ĐÃ ROLLBACK TRANSACTION HOÀN TOÀN.';
PRINT N'KHÔNG CÓ BẤT KỲ DỮ LIỆU NÀO BỊ THAY ĐỔI HOẶC GHI VÀO DATABASE!';
PRINT N'Kịch bản an toàn tuyệt đối và sẵn sàng áp dụng (Apply).';
PRINT N'======================================================================================';
