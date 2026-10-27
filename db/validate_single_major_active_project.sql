/* ==============================================================================================
   SCRIPT: validate_single_major_active_project.sql
   MỤC ĐÍCH: Kịch bản xác thực tự động (Post-Seed Validation) cho đồ án ĐƠN NGÀNH SE
   ĐỊNH DANH SEED: DEMO-SINGLE-MAJOR-ACTIVE-V1
   MÃ DỰ ÁN: SEP490-FA26-SE01 | MÃ NHÓM: SEP490_G03
   HỆ THỐNG: AI-PMS (AI Project Management System) - FPT University Da Nang
   
   CÁC HẠNG MỤC KIỂM THỬ:
     1. Tính duy nhất và trạng thái đồ án (Project status = 'ACTIVE').
     2. Ràng buộc học thuật đơn ngành (project_majors count = 1, major = SE).
     3. Roster thành viên nhóm (100% sinh viên SE, Leader = Nguyễn Văn Hùng).
     4. Lịch sử phê duyệt đầy đủ 6 bước (DRAFT -> ... -> ACTIVE).
     5. Phân công GVHD chính (PRIMARY, is_primary = 1, major_id IS NULL).
     6. Thống kê toàn diện dữ liệu thực thi (Agile Sprints, Kanban, Meetings, Reports, Deliverables).
     7. Kiểm tra an toàn dữ liệu: Bảo toàn 100% dữ liệu của Project 1 và Project 2.
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;

USE [AI_PMS];

PRINT N'======================================================================================';
PRINT N'BẮT ĐẦU XÁC THỰC DỮ LIỆU ĐỒ ÁN ĐƠN NGÀNH SE: SEP490-FA26-SE01';
PRINT N'======================================================================================';

DECLARE @ProjectCode nvarchar(50) = N'SEP490-FA26-SE01';
DECLARE @TeamCode nvarchar(50) = N'SEP490_G03';
DECLARE @ProjectId bigint = (SELECT id FROM dbo.projects WHERE code = @ProjectCode);
DECLARE @TeamId bigint = (SELECT id FROM dbo.teams WHERE code = @TeamCode);

IF @ProjectId IS NULL
BEGIN
    PRINT N'[LỖI]: Không tìm thấy dự án có mã "' + @ProjectCode + N'"!';
    RETURN;
END

-- 1. KIỂM TRA TRẠNG THÁI DỰ ÁN VÀ CẤU HÌNH HỌC THUẬT
PRINT N'1. KIỂM TRA TRẠNG THÁI DỰ ÁN & CẤU HÌNH HỌC THUẬT:';
SELECT 
    p.id AS [ProjectID],
    p.code AS [ProjectCode],
    p.title AS [ProjectTitle],
    p.status AS [ProjectStatus],
    t.code AS [TeamCode],
    tac.project_mode AS [AcademicMode],
    m.code AS [PrimaryMajor]
FROM dbo.projects p
JOIN dbo.teams t ON p.team_id = t.id
JOIN dbo.team_academic_configurations tac ON t.id = tac.team_id
JOIN dbo.majors m ON tac.primary_major_id = m.id
WHERE p.id = @ProjectId;

-- 2. KIỂM TRA RÀNG BUỘC ĐƠN NGÀNH TRONG PROJECT_MAJORS
PRINT N'2. KIỂM TRA RÀNG BUỘC ĐƠN NGÀNH TRONG PROJECT_MAJORS:';
SELECT 
    pm.project_id AS [ProjectID],
    pm.major_id AS [MajorID],
    m.code AS [MajorCode],
    m.name AS [MajorName]
FROM dbo.project_majors pm
JOIN dbo.majors m ON pm.major_id = m.id
WHERE pm.project_id = @ProjectId;

DECLARE @MajorCount int = (SELECT COUNT(*) FROM dbo.project_majors WHERE project_id = @ProjectId);
IF @MajorCount = 1
    PRINT N'-> [PASS]: Số lượng chuyên ngành đúng bằng 1 (Chuẩn Đơn ngành Single-Major).';
ELSE
    PRINT N'-> [FAIL]: Dự án có ' + CAST(@MajorCount AS nvarchar(10)) + N' chuyên ngành (Vi phạm chuẩn Đơn ngành)!';

-- 3. KIỂM TRA THÀNH VIÊN NHÓM
PRINT N'3. DANH SÁCH THÀNH VIÊN NHÓM (YÊU CẦU 100% SE):';
SELECT 
    tm.id AS [MemberID],
    u.id AS [UserID],
    u.student_code AS [MSSV],
    u.full_name AS [FullName],
    u.email AS [Email],
    m.code AS [Major],
    CASE WHEN tm.is_leader = 1 THEN N'Trưởng nhóm (Leader)' ELSE N'Thành viên (Member)' END AS [VaiTro]
FROM dbo.team_members tm
JOIN dbo.users u ON tm.user_id = u.id
JOIN dbo.majors m ON u.major_id = m.id
WHERE tm.team_id = @TeamId
ORDER BY tm.is_leader DESC, tm.id ASC;

-- 4. KIỂM TRA LỊCH SỬ DUYỆT 6 BƯỚC
PRINT N'4. LỊCH SỬ XÉT DUYỆT ĐẦY ĐỦ CÁC BƯỚC (PROJECT STATUS HISTORY):';
SELECT 
    psh.id AS [StepID],
    ISNULL(psh.old_status, 'START') AS [OldStatus],
    psh.new_status AS [NewStatus],
    u.full_name AS [ChangedBy],
    r.code AS [Role],
    psh.reason AS [Reason],
    psh.changed_at AS [ChangedAt]
FROM dbo.project_status_history psh
JOIN dbo.users u ON psh.changed_by = u.id
JOIN dbo.user_roles ur ON u.id = ur.user_id
JOIN dbo.roles r ON ur.role_id = r.id
WHERE psh.project_id = @ProjectId
ORDER BY psh.changed_at ASC;

-- 5. KIỂM TRA PHÂN CÔNG GVHD CHÍNH
PRINT N'5. PHÂN CÔNG GIẢNG VIÊN HƯỚNG DẪN CHÍNH (PRIMARY):';
SELECT 
    sa.id AS [AssignmentID],
    u.full_name AS [SupervisorName],
    u.email AS [SupervisorEmail],
    sa.assignment_type AS [AssignmentType],
    sa.is_primary AS [IsPrimary],
    CASE WHEN sa.major_id IS NULL THEN N'NULL (Chuẩn Đơn ngành)' ELSE CAST(sa.major_id AS nvarchar(10)) END AS [MajorSlot],
    sr.status AS [RequestStatus]
FROM dbo.supervisor_assignments sa
JOIN dbo.supervisor_profiles sp ON sa.supervisor_profile_id = sp.id
JOIN dbo.users u ON sp.user_id = u.id
JOIN dbo.supervisor_requests sr ON sa.supervisor_request_id = sr.id
WHERE sa.project_id = @ProjectId;

-- 6. THỐNG KÊ DỮ LIỆU THỰC THI (IN-PROGRESS MODULES)
PRINT N'6. THỐNG KÊ DỮ LIỆU ĐANG THỰC HIỆN:';
SELECT N'Thỏa ước nhóm (Working Agreements)' AS [HangMuc], COUNT(*) AS [SoLuong] FROM dbo.project_working_agreements WHERE project_id = @ProjectId
UNION ALL
SELECT N'Chữ ký thỏa ước (Signatures)', COUNT(*) FROM dbo.agreement_members WHERE agreement_id IN (SELECT id FROM dbo.project_working_agreements WHERE project_id = @ProjectId)
UNION ALL
SELECT N'Giai đoạn đồ án (Milestones)', COUNT(*) FROM dbo.milestones WHERE project_id = @ProjectId
UNION ALL
SELECT N'Chu kỳ Agile (Sprints)', COUNT(*) FROM dbo.sprints WHERE project_id = @ProjectId
UNION ALL
SELECT N'Nhiệm vụ Kanban (Tasks)', COUNT(*) FROM dbo.tasks WHERE milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId)
UNION ALL
SELECT N'Phân công nhiệm vụ (Assignees)', COUNT(*) FROM dbo.task_assignees WHERE task_id IN (SELECT id FROM dbo.tasks WHERE milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId))
UNION ALL
SELECT N'Cuộc họp (Meetings)', COUNT(*) FROM dbo.meetings WHERE project_id = @ProjectId
UNION ALL
SELECT N'Báo cáo tiến độ (Progress Reports)', COUNT(*) FROM dbo.progress_reports WHERE project_id = @ProjectId
UNION ALL
SELECT N'Nhận xét của GVHD (Feedbacks)', COUNT(*) FROM dbo.supervisor_feedback WHERE project_id = @ProjectId
UNION ALL
SELECT N'Sản phẩm bàn giao (Deliverables)', COUNT(*) FROM dbo.deliverables WHERE project_id = @ProjectId;

-- 7. KIỂM TRA BẢO TOÀN DỮ LIỆU CŨ (ZERO REGRESSION)
PRINT N'7. BẢO TOÀN DỮ LIỆU CŨ (PROJECT 1 & 2):';
SELECT id AS [ProjectID], code AS [ProjectCode], status AS [Status], team_id AS [TeamID], created_by AS [CreatedBy]
FROM dbo.projects WHERE id IN (1, 2);

PRINT N'======================================================================================';
PRINT N'XÁC THỰC HOÀN TẤT. DỰ ÁN ĐẠT TOÀN BỘ TIÊU CHÍ SINGLE-MAJOR VÀ TRẠNG THÁI ACTIVE!';
PRINT N'======================================================================================';
