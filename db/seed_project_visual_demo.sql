/* ==============================================================================================
   SCRIPT: seed_project_visual_demo.sql
   MỤC ĐÍCH: Khởi tạo & làm giàu dữ liệu trực quan (Visual Demo Data) cho dự án Capstone Project
   HỆ THỐNG: AI-PMS (AI Project Management System)
   TÍNH NĂNG ĐƯỢC LÀM GIÀU:
     1. Chuẩn hóa tên tiếng Việt có dấu cho thành viên & Giảng viên hướng dẫn (Unicode)
     2. Giai đoạn dự án (Milestones - 4 mốc: Proposal, Architecture, AI & Execution, Defense)
     3. Chu kỳ Sprint Agile (Sprints - Sprint 1: COMPLETED, Sprint 2: ACTIVE, Sprint 3: PLANNED)
     4. Nhiệm vụ Kanban (Tasks - Phân bổ đầy đủ các cột: TODO, IN_PROGRESS, BLOCKED, IN_REVIEW, DONE)
     5. Điểm Story Points (Fibonacci: 1, 2, 3, 5, 8) & Phân công thành viên (Task Assignees)
     6. Sản phẩm nộp & Phiên bản (Deliverables & Deliverable Versions & Attached Files)
     7. Lịch họp & Biên bản họp (Meetings, Attendees, Decisions, Action Items)
     8. Báo cáo tiến độ chu kỳ (Progress Report Periods & Rich Markdown Progress Reports)
     9. Phản hồi của Giảng viên hướng dẫn (Supervisor Feedback)
    10. Thỏa ước làm việc nhóm (Team Working Agreement & Member Acceptances)
   
   HƯỚNG DẪN SỬ DỤNG & CẢNH BÁO MÔI TRƯỜNG:
     - CHỈ SỬ DỤNG TRÊN DATABASE DEMO / STAGING CÔ LẬP.
     - TUYỆT ĐỐI KHÔNG CHẠY TRÊN DATABASE PRODUCTION / DÙNG CHUNG CỦA TRƯỜNG HOẶC ĐANG CÓ DỮ LIỆU THẬT CỦA CÁC NHÓM KHÁC.
     - Yêu cầu đúng đồ án fixture mục tiêu (mặc định: 'SEP490-FA26-AIPMS'). Nếu không tìm thấy, script sẽ ROLLBACK và dừng an toàn ngay lập tức.
     - Bảo toàn dữ liệu hiện có: Script chỉ tạo mới các thực thể chưa tồn tại; không ghi đè title dự án, không ép lifecycle ACTIVE và không ghi đè nội dung người dùng đã chỉnh sửa.
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

-- USE [AI_PMS]; -- (Mặc định chạy trên database đang được chọn trong SSMS)

-- ==============================================================================================
-- 0. CẤU HÌNH THÔNG TIN DỰ ÁN MỤC TIÊU (CONFIGURABLE TARGET PROJECT)
-- ==============================================================================================
DECLARE @TargetProjectCode nvarchar(50) = N'SEP490-FA26-AIPMS';

BEGIN TRANSACTION;

-- [SECURITY & DATA ISOLATION GUARD]
-- Bắt buộc đúng mã fixture demo được chỉ định; tuyệt đối KHÔNG fallback sang đồ án khác để tránh ghi đè nhầm dữ liệu nghiệp vụ thật
DECLARE @ProjectId bigint = (SELECT id FROM dbo.projects WHERE code = @TargetProjectCode);

IF @ProjectId IS NULL
BEGIN
    RAISERROR(N'[SECURITY & ISOLATION CHECK]: Không tìm thấy đồ án fixture có mã "%s". Script dừng an toàn (ROLLBACK) và không thay đổi bất kỳ dữ liệu nào!', 16, 1, @TargetProjectCode);
    ROLLBACK TRANSACTION;
    RETURN;
END

PRINT N'--------------------------------------------------------------------------------------';
PRINT N'ĐANG BẮT ĐẦU SEED DỮ LIỆU TRỰC QUAN CHO DỰ ÁN: ' + @TargetProjectCode + N' (ID: ' + CAST(@ProjectId as nvarchar(10)) + N')';
PRINT N'--------------------------------------------------------------------------------------';

DECLARE @TeamId bigint = (SELECT team_id FROM dbo.projects WHERE id = @ProjectId);
DECLARE @CreatedBy bigint = (SELECT created_by FROM dbo.projects WHERE id = @ProjectId);
DECLARE @SemesterId bigint = (SELECT academic_semester_id FROM dbo.teams WHERE id = @TeamId);
DECLARE @PeriodId bigint = (SELECT TOP 1 id FROM dbo.project_periods WHERE academic_semester_id = @SemesterId AND period_type = 'PROJECT_EXECUTION' ORDER BY id);
IF @PeriodId IS NULL
    SET @PeriodId = (SELECT TOP 1 id FROM dbo.project_periods WHERE code = N'SEP490-FA26-EXEC');
IF @PeriodId IS NULL
    SET @PeriodId = (SELECT TOP 1 id FROM dbo.project_periods ORDER BY id);

-- Xác định các thành viên nhóm
DECLARE @LeaderUserId bigint = (
    SELECT TOP 1 tm.user_id 
    FROM dbo.team_members tm 
    WHERE tm.team_id = @TeamId AND tm.is_leader = 1
);
IF @LeaderUserId IS NULL
    SET @LeaderUserId = @CreatedBy;

-- Lấy danh sách thành viên nhóm vào bảng tạm
DECLARE @Members TABLE (
    idx int identity(1,1),
    team_member_id bigint,
    user_id bigint,
    is_leader bit,
    full_name nvarchar(200),
    membership_generation uniqueidentifier
);

INSERT INTO @Members (team_member_id, user_id, is_leader, full_name, membership_generation)
SELECT tm.id, tm.user_id, tm.is_leader, u.full_name, tm.membership_generation
FROM dbo.team_members tm
JOIN dbo.users u ON tm.user_id = u.id
WHERE tm.team_id = @TeamId
ORDER BY tm.is_leader DESC, tm.id ASC;

DECLARE @User1 bigint = (SELECT user_id FROM @Members WHERE idx = 1); -- Trưởng nhóm
DECLARE @User2 bigint = (SELECT user_id FROM @Members WHERE idx = 2); -- Thành viên 2
DECLARE @User3 bigint = (SELECT user_id FROM @Members WHERE idx = 3); -- Thành viên 3
DECLARE @User4 bigint = (SELECT user_id FROM @Members WHERE idx = 4); -- Thành viên 4
DECLARE @User5 bigint = (SELECT user_id FROM @Members WHERE idx = 5); -- Thành viên 5

-- Fallback nếu nhóm ít hơn 5 thành viên
IF @User2 IS NULL SET @User2 = @User1;
IF @User3 IS NULL SET @User3 = @User1;
IF @User4 IS NULL SET @User4 = @User2;
IF @User5 IS NULL SET @User5 = @User3;

-- Xác định Giảng viên hướng dẫn (Supervisor)
DECLARE @SupervisorUserId bigint = (
    SELECT TOP 1 sp.user_id
    FROM dbo.supervisor_assignments sa
    JOIN dbo.supervisor_profiles sp ON sa.supervisor_profile_id = sp.id
    WHERE sa.project_id = @ProjectId AND sa.is_primary = 1
);
DECLARE @SupervisorAssignmentId bigint = (
    SELECT TOP 1 sa.id
    FROM dbo.supervisor_assignments sa
    WHERE sa.project_id = @ProjectId AND sa.is_primary = 1
);
IF @SupervisorUserId IS NULL
BEGIN
    SELECT TOP 1 @SupervisorUserId = sp.user_id, @SupervisorAssignmentId = sa.id
    FROM dbo.supervisor_assignments sa
    JOIN dbo.supervisor_profiles sp ON sa.supervisor_profile_id = sp.id
    WHERE sa.project_id = @ProjectId;
END
IF @SupervisorUserId IS NULL
    SET @SupervisorUserId = (SELECT TOP 1 id FROM dbo.users WHERE email LIKE N'lecturer%');

-- ==============================================================================================
-- 1. CHUẨN HÓA TÊN TIẾNG VIỆT CÓ DẤU (UNICODE FULL NAMES)
-- ==============================================================================================
PRINT N'1. Chuẩn hóa tên hiển thị tiếng Việt có dấu cho người dùng (bảo toàn thay đổi nếu đã được chỉnh sửa)...';

-- [USER INTEGRITY GUARD]: Chỉ chuẩn hóa tên có dấu cho thành viên và GVHD của đồ án mục tiêu nếu chưa từng được chỉnh sửa (giữ nguyên nếu đã đổi tên)
UPDATE u 
SET u.full_name = v.default_name
FROM dbo.users u
JOIN (VALUES 
    (N'khangnmde180901@fpt.edu.vn', N'Nguyễn Minh Khang'),
    (N'hantgde180902@fpt.edu.vn',   N'Trần Gia Hân'),
    (N'baopqde180903@fpt.edu.vn',   N'Phạm Quốc Bảo'),
    (N'linhlkde180904@fpt.edu.vn',  N'Lê Khánh Linh'),
    (N'duyvade180905@fpt.edu.vn',   N'Võ Anh Duy'),
    (N'maintde180906@fpt.edu.vn',   N'Ngô Thanh Mai')
) AS v(email, default_name) ON u.email = v.email
WHERE u.id IN (SELECT user_id FROM @Members)
  AND (u.full_name IS NULL 
       OR u.full_name = N'' 
       OR u.full_name = u.email 
       OR u.full_name = LEFT(u.email, CHARINDEX('@', u.email) - 1));

IF @SupervisorUserId IS NOT NULL
BEGIN
    UPDATE u
    SET u.full_name = N'Nguyễn Hoàng Minh'
    FROM dbo.users u
    WHERE u.id = @SupervisorUserId
      AND u.email = N'lecturer.se01@fe.edu.vn'
      AND (u.full_name IS NULL 
           OR u.full_name = N'' 
           OR u.full_name = u.email 
           OR u.full_name = LEFT(u.email, CHARINDEX('@', u.email) - 1));
END

-- [LIFECYCLE & CONTENT INTEGRITY GUARD]
-- Chỉ bổ sung nội dung mô tả nếu đồ án chưa có nội dung; KHÔNG ghi đè title hoặc ép trạng thái ACTIVE nếu dự án đang ở vòng đời khác
UPDATE dbo.projects 
SET description = ISNULL(NULLIF(description, N''), N'Nền tảng tích hợp theo dõi tiến độ đồ án tốt nghiệp, phân tích mã nguồn tự động, hỗ trợ phân công nhiệm vụ và đánh giá năng lực liên ngành ứng dụng Trí tuệ Nhân tạo.'),
    problem_statement = ISNULL(NULLIF(problem_statement, N''), N'Quy trình quản lý đồ án Capstone hiện nay thiếu công cụ theo dõi realtime, đánh giá chéo giữa các chuyên ngành còn rời rạc và việc phát hiện rủi ro chậm trễ còn mang tính thủ công.'),
    expected_output = ISNULL(NULLIF(expected_output, N''), N'Hệ thống Web App hoàn chỉnh gồm Portal Sinh viên, Giảng viên, Hội đồng bảo vệ, tích hợp Agentic AI phân tích tiến độ và cảnh báo nguy cơ trễ hạn.')
WHERE id = @ProjectId;

-- ==============================================================================================
-- 2. GIAI ĐOẠN DỰ ÁN (MILESTONES)
-- ==============================================================================================
PRINT N'2. Thiết lập 4 giai đoạn Milestone chuẩn...';

-- Đảm bảo có đủ 4 Milestones chính
DECLARE @M1_Id bigint, @M2_Id bigint, @M3_Id bigint, @M4_Id bigint;

SELECT @M1_Id = id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 1;
IF @M1_Id IS NULL
BEGIN
    INSERT INTO dbo.milestones (project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token)
    VALUES (@ProjectId, N'Giai đoạn 1: Khảo sát & Đề cương dự án (Proposal & Requirements)', 
            N'Phân tích nghiệp vụ Capstone, khảo sát yêu cầu người dùng, hoàn thiện tài liệu Software Requirement Specification (SRS) và bảo vệ đề cương.',
            '2026-09-01', '2026-09-15', 'COMPLETED', 1, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());
    SET @M1_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

SELECT @M2_Id = id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 2;
IF @M2_Id IS NULL
BEGIN
    INSERT INTO dbo.milestones (project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token)
    VALUES (@ProjectId, N'Giai đoạn 2: Thiết kế kiến trúc & Core MVP (Architecture & Core MVP)', 
            N'Thiết kế CSDL quan hệ 3NF, kiến trúc phân tầng Clean Architecture, xây dựng API xác thực JWT, RBAC và khung giao diện chính.',
            '2026-09-16', '2026-10-15', 'COMPLETED', 2, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());
    SET @M2_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

SELECT @M3_Id = id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 3;
IF @M3_Id IS NULL
BEGIN
    INSERT INTO dbo.milestones (project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token)
    VALUES (@ProjectId, N'Giai đoạn 3: Phát triển tính năng nâng cao & Tích hợp AI (Advanced Features & AI)', 
            N'Triển khai quản lý Sprint/Task, Báo cáo tiến độ tuần/tháng, Tích hợp module AI Rule-Based/LLM phân tích tiến độ, Họp trực tuyến LiveKit.',
            '2026-10-16', '2026-11-30', 'IN_PROGRESS', 3, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());
    SET @M3_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

SELECT @M4_Id = id FROM dbo.milestones WHERE project_id = @ProjectId AND sort_order = 4;
IF @M4_Id IS NULL
BEGIN
    INSERT INTO dbo.milestones (project_id, title, description, start_date, due_date, status, sort_order, created_by, created_at, updated_at, concurrency_token)
    VALUES (@ProjectId, N'Giai đoạn 4: Kiểm thử toàn diện, Đóng gói & Bảo vệ tốt nghiệp (Defense)', 
            N'Kiểm thử tự động End-to-End, hoàn thiện tài liệu đồ án kỹ thuật, đóng gói sản phẩm và thuyết trình trước Hội đồng chấm tốt nghiệp.',
            '2026-12-01', '2026-12-31', 'PLANNED', 4, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());
    SET @M4_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- ==============================================================================================
-- 3. CHU KỲ SPRINT (SPRINTS)
-- ==============================================================================================
PRINT N'3. Thiết lập 3 Chu kỳ Sprint Agile (COMPLETED, ACTIVE, PLANNED)...';

DECLARE @Sprint1_Id bigint, @Sprint2_Id bigint, @Sprint3_Id bigint;

-- Sprint 1: COMPLETED (Từ 16/09/2026 đến 05/10/2026)
SELECT @Sprint1_Id = id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 1%';
IF @Sprint1_Id IS NULL
BEGIN
    INSERT INTO dbo.sprints (project_id, name, status, start_at, end_at, started_at, completed_at, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, N'Sprint 1 - Thiết kế CSDL & Xác thực Core', 'COMPLETED', 
            '2026-09-16 00:00:00', '2026-10-05 23:59:59', '2026-09-16 08:00:00', '2026-10-05 17:30:00',
            NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @Sprint1_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Sprint 2: ACTIVE (Từ 06/10/2026 đến 27/10/2026)
SELECT @Sprint2_Id = id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 2%';
IF @Sprint2_Id IS NULL
BEGIN
    INSERT INTO dbo.sprints (project_id, name, status, start_at, end_at, started_at, completed_at, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, N'Sprint 2 - Quản lý Nhiệm vụ, Báo cáo & Tích hợp AI', 'ACTIVE', 
            '2026-10-06 00:00:00', '2026-10-27 23:59:59', '2026-10-06 08:30:00', NULL,
            NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @Sprint2_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Sprint 3: PLANNED (Từ 28/10/2026 đến 20/11/2026)
SELECT @Sprint3_Id = id FROM dbo.sprints WHERE project_id = @ProjectId AND name LIKE N'Sprint 3%';
IF @Sprint3_Id IS NULL
BEGIN
    INSERT INTO dbo.sprints (project_id, name, status, start_at, end_at, started_at, completed_at, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, N'Sprint 3 - Tối ưu UI/UX, Đánh giá chéo & Nghiệm thu', 'PLANNED', 
            '2026-10-28 00:00:00', '2026-11-20 23:59:59', NULL, NULL,
            NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @Sprint3_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- ==============================================================================================
-- 4. NHIỆM VỤ KANBAN & STORY POINTS (TASKS & ASSIGNEES)
-- ==============================================================================================
PRINT N'4. Cập nhật và bổ sung danh sách nhiệm vụ đa dạng trên Kanban Board...';

-- Danh sách nhiệm vụ mô phỏng phong phú cho dự án AI-PMS
DECLARE @TaskData TABLE (
    keyword nvarchar(100),
    title nvarchar(300),
    description nvarchar(1000),
    status nvarchar(20),
    priority nvarchar(20),
    story_points int,
    milestone_id bigint,
    sprint_id bigint,
    start_at datetime2,
    due_at datetime2,
    completed_at datetime2,
    assignee1 bigint,
    assignee2 bigint
);

INSERT INTO @TaskData VALUES
-- Giai đoạn 1 & Sprint 1 (DONE)
(N'%workflow%', N'Khảo sát quy trình quản lý đồ án Capstone và luồng nghiệp vụ thực tế', 
 N'Phỏng vấn các chủ nhiệm bộ môn SE/AI/BA, tổng hợp quy định Capstone FPT và lập bảng ma trận yêu cầu chức năng.', 
 'DONE', 'HIGH', 5, @M1_Id, @Sprint1_Id, '2026-09-02 08:00', '2026-09-07 17:00', '2026-09-06 16:30', @User1, @User4),

(N'%ERD%', N'Thiết kế mô hình dữ liệu quan hệ ERD chuẩn 3NF trên SQL Server', 
 N'Xây dựng các bảng người dùng, vai trò, nhóm, dự án, giai đoạn, nhiệm vụ kèm đầy đủ khóa ngoại và check constraints.', 
 'DONE', 'CRITICAL', 8, @M1_Id, @Sprint1_Id, '2026-09-08 08:00', '2026-09-14 17:00', '2026-09-13 18:00', @User2, @User5),

(N'%proposal%', N'Soạn thảo đề cương chi tiết dự án (Proposal Document v1.0 & v2.0)', 
 N'Viết tài liệu thuyết minh đề tài, xác định bài toán, phương pháp luận và các kết quả dự kiến bàn giao.', 
 'DONE', 'MEDIUM', 3, @M1_Id, @Sprint1_Id, '2026-09-05 08:00', '2026-09-12 17:00', '2026-09-11 15:00', @User4, @User1),

(N'%authentication%', N'Xây dựng dịch vụ Xác thực tập trung JWT và Phân quyền RBAC', 
 N'Cài đặt Identity, mã hóa mật khẩu PBKDF2/BCrypt, middleware xác thực Bearer token và kiểm tra quyền RBAC theo endpoint.', 
 'DONE', 'HIGH', 5, @M2_Id, @Sprint1_Id, '2026-09-17 08:00', '2026-09-24 17:00', '2026-09-23 20:00', @User2, @User1),

(N'%team and project%', N'Triển khai các API quản lý Nhóm và Hồ sơ dự án', 
 N'Endpoints CRUD Team, đăng ký đề tài, duyệt thành viên, upload tài liệu đính kèm và đồng bộ trạng thái.', 
 'DONE', 'HIGH', 5, @M2_Id, @Sprint1_Id, '2026-09-25 08:00', '2026-10-02 17:00', '2026-10-01 17:00', @User5, @User2),

(N'%dashboard%', N'Thiết kế giao diện Dashboard Sinh viên và Giảng viên với React & TailwindCSS', 
 N'Dựng layout responsive, thanh điều hướng, card thống kê tiến độ, biểu đồ hoàn thành và widget thông báo.', 
 'DONE', 'MEDIUM', 3, @M2_Id, @Sprint1_Id, '2026-09-28 08:00', '2026-10-04 17:00', '2026-10-04 11:30', @User1, @User4),

-- Giai đoạn 2/3 & Sprint 2 (ĐANG DIỄN RA - Đa dạng trạng thái)
(N'%milestone and task%', N'Xây dựng Bảng Kanban tương tác kéo thả và Quản lý Sprint Backlog', 
 N'Cho phép chuyển trạng thái Task trực quan giữa các cột TODO, IN_PROGRESS, IN_REVIEW, DONE với cập nhật realtime.', 
 'IN_PROGRESS', 'HIGH', 8, @M3_Id, @Sprint2_Id, '2026-10-07 08:00', '2026-10-22 17:00', NULL, @User1, @User2),

(N'%progress reports%', N'Xây dựng module Báo cáo tiến độ chu kỳ tuần/tháng với trình soạn thảo Markdown', 
 N'Cho phép sinh viên soạn thảo trực quan nội dung công việc hoàn thành, rủi ro, dự kiến tuần tới kèm đính kèm file.', 
 'IN_REVIEW', 'HIGH', 5, @M3_Id, @Sprint2_Id, '2026-10-08 08:00', '2026-10-21 17:00', NULL, @User1, @User4),

(N'%metadata repository%', N'Tích hợp Google Drive API lưu trữ tài liệu minh chứng nộp bài', 
 N'Đang chờ Google Cloud Console phê duyệt OAuth 2.0 Consent Screen cho tài khoản FPT Enterprise.', 
 'BLOCKED', 'MEDIUM', 3, @M3_Id, @Sprint2_Id, '2026-10-14 08:00', '2026-10-24 17:00', NULL, @User5, @User3),

(N'%AI Rule-Based%', N'Tích hợp module AI Rule-Based & Semantic Context phân tích tiến độ dự án', 
 N'Xây dựng logic tự động đánh giá nguy cơ chậm hạn dựa trên số lượng Task tồn đọng và lịch sử nộp báo cáo tuần.', 
 'IN_PROGRESS', 'CRITICAL', 8, @M3_Id, @Sprint2_Id, '2026-10-10 08:00', '2026-10-25 17:00', NULL, @User3, @User5),

(N'%SignalR%', N'Tích hợp Webhook thông báo thời gian thực qua SignalR Hub', 
 N'Đang gặp lỗi xung đột CORS và kết nối Websocket chập chờn khi chạy qua Cloudflare Reverse Proxy.', 
 'BLOCKED', 'HIGH', 5, @M3_Id, @Sprint2_Id, '2026-10-12 08:00', '2026-10-20 17:00', NULL, @User2, @User5),

(N'%Deliverables nhiều phiên bản%', N'Xây dựng module Nộp và Phê duyệt Deliverables nhiều phiên bản', 
 N'Đã hoàn thành code và đang trong giai đoạn review chéo mã nguồn (Pull Request #14).', 
 'IN_REVIEW', 'HIGH', 5, @M3_Id, @Sprint2_Id, '2026-10-08 08:00', '2026-10-19 17:00', NULL, @User4, @User1),

(N'%khuyến nghị tự động%', N'Nghiên cứu áp dụng LLM tạo khuyến nghị tự động cho Giảng viên hướng dẫn', 
 N'Thu thập dataset mẫu các phản hồi đồ án, tinh chỉnh system prompt để AI tóm tắt ngắn gọn các điểm nghẽn của nhóm.', 
 'TODO', 'HIGH', 5, @M3_Id, @Sprint2_Id, '2026-10-22 08:00', '2026-10-27 17:00', NULL, @User3, @User1),

-- Sprint 3 (PLANNED)
(N'%Peer Evaluation%', N'Xây dựng hệ thống Đánh giá chéo thành viên nội bộ (Peer Evaluation)', 
 N'Cài đặt tiêu chí chấm điểm đóng góp cá nhân theo thang điểm 5 sao, tính toán tỷ lệ % phân bổ nỗ lực.', 
 'TODO', 'HIGH', 5, @M3_Id, @Sprint3_Id, '2026-10-28 08:00', '2026-11-10 17:00', NULL, @User4, @User2),

(N'%technical report%', N'Soạn thảo Báo cáo tổng kết đồ án tốt nghiệp kỹ thuật (Final Technical Report)', 
 N'Tổng hợp toàn bộ kiến trúc hệ thống, kết quả thực nghiệm, so sánh hiệu năng và bài học kinh nghiệm.', 
 'TODO', 'HIGH', 8, @M4_Id, @Sprint3_Id, '2026-11-01 08:00', '2026-12-15 17:00', NULL, @User1, @User4),

(N'%defense presentation%', N'Chuẩn bị Slide thuyết trình và Kịch bản Demo bảo vệ tốt nghiệp', 
 N'Biên soạn slide trình chiếu theo mẫu chuẩn FPT University và video quay sẵn dự phòng khi bảo vệ trước Hội đồng.', 
 'TODO', 'MEDIUM', 3, @M4_Id, @Sprint3_Id, '2026-11-10 08:00', '2026-12-20 17:00', NULL, @User2, @User5),

(N'%Kiểm thử hiệu năng%', N'Kiểm thử hiệu năng chịu tải API với k6 và Tối ưu hóa truy vấn SQL', 
 N'Viết kịch bản test 500 người dùng đồng thời, đánh index các cột tìm kiếm và cache dữ liệu tĩnh.', 
 'TODO', 'MEDIUM', 5, @M4_Id, @Sprint3_Id, '2026-11-05 08:00', '2026-11-18 17:00', NULL, @User5, @User3),

(N'%Docker Compose%', N'Đóng gói Docker Compose và thiết lập CI/CD pipeline tự động deploy lên VPS', 
 N'Cấu hình Dockerfile đa tầng cho Frontend Vite và Backend .NET 8, tự động build & deploy khi merge develop.', 
 'DONE', 'MEDIUM', 3, @M2_Id, @Sprint1_Id, '2026-10-01 08:00', '2026-10-05 17:00', '2026-10-05 16:00', @User5, @User2);

-- Cập nhật hoặc thêm mới các task (Lưu ý: task_code là computed column nên không insert/update)
DECLARE @curKeyword nvarchar(100), @curTitle nvarchar(300), @curDesc nvarchar(1000);
DECLARE @curStatus nvarchar(20), @curPriority nvarchar(20), @curPoints int;
DECLARE @curMId bigint, @curSId bigint, @curStart datetime2, @curDue datetime2, @curComp datetime2;
DECLARE @curA1 bigint, @curA2 bigint;

DECLARE task_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT keyword, title, description, status, priority, story_points, milestone_id, sprint_id, start_at, due_at, completed_at, assignee1, assignee2
FROM @TaskData;

OPEN task_cursor;
FETCH NEXT FROM task_cursor INTO @curKeyword, @curTitle, @curDesc, @curStatus, @curPriority, @curPoints, @curMId, @curSId, @curStart, @curDue, @curComp, @curA1, @curA2;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @ExistingTaskId bigint = (
        SELECT TOP 1 t.id 
        FROM dbo.tasks t 
        WHERE t.milestone_id IN (@M1_Id, @M2_Id, @M3_Id, @M4_Id) 
          AND (t.title = @curTitle OR (@curKeyword IS NOT NULL AND t.title LIKE @curKeyword))
    );
    
    IF @ExistingTaskId IS NULL
    BEGIN
        INSERT INTO dbo.tasks (milestone_id, title, description, status, priority, story_points, sprint_id, start_at, due_at, completed_at, created_by, created_at, updated_at, concurrency_token)
        VALUES (@curMId, @curTitle, @curDesc, @curStatus, @curPriority, @curPoints, @curSId, @curStart, @curDue, @curComp, @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID());
        SET @ExistingTaskId = SCOPE_IDENTITY();
    END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

    -- Gán Assignees
    IF @curA1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.task_assignees WHERE task_id = @ExistingTaskId AND user_id = @curA1)
    BEGIN
        INSERT INTO dbo.task_assignees (task_id, user_id, assigned_by, assigned_at)
        VALUES (@ExistingTaskId, @curA1, @LeaderUserId, GETUTCDATE());
    END

    IF @curA2 IS NOT NULL AND @curA2 <> @curA1 AND NOT EXISTS (SELECT 1 FROM dbo.task_assignees WHERE task_id = @ExistingTaskId AND user_id = @curA2)
    BEGIN
        INSERT INTO dbo.task_assignees (task_id, user_id, assigned_by, assigned_at)
        VALUES (@ExistingTaskId, @curA2, @LeaderUserId, GETUTCDATE());
    END

    FETCH NEXT FROM task_cursor INTO @curKeyword, @curTitle, @curDesc, @curStatus, @curPriority, @curPoints, @curMId, @curSId, @curStart, @curDue, @curComp, @curA1, @curA2;
END

CLOSE task_cursor;
DEALLOCATE task_cursor;

-- ==============================================================================================
-- 5. SẢN PHẨM BÀN GIAO & PHIÊN BẢN (DELIVERABLES & VERSIONS)
-- ==============================================================================================
PRINT N'5. Thiết lập Sản phẩm bàn giao (Deliverables) & Phiên bản...';

-- Deliverable 1: Đề cương chi tiết SRS (M1)
DECLARE @Deliv1_Id bigint = (SELECT TOP 1 id FROM dbo.deliverables WHERE project_id = @ProjectId AND (milestone_id = @M1_Id OR title LIKE N'%Proposal%' OR title LIKE N'%SRS%' OR title LIKE N'%Đề cương%'));
IF @Deliv1_Id IS NULL
BEGIN
    INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at, evidence_scope)
    VALUES (@ProjectId, @M1_Id, N'Đề cương chi tiết & Đặc tả yêu cầu phần mềm (SRS)', 
            N'Bao gồm bản đặc tả yêu cầu người dùng, use case specifications, sơ đồ quy trình nghiệp vụ BPMN và kế hoạch thực hiện đồ án.',
            N'DOCUMENT', '2026-09-15 17:00:00', 'ACCEPTED', @LeaderUserId, GETUTCDATE(), GETUTCDATE(), 'COMMON');
    SET @Deliv1_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Versions cho Deliv 1 (v1 REJECTED, v2 ACCEPTED)
IF NOT EXISTS (SELECT 1 FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv1_Id AND version_number = 1)
BEGIN
    INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
    VALUES (@Deliv1_Id, 1, @LeaderUserId, N'Nộp bản thảo đề cương lần 1 để GVHD góp ý sơ bộ.', 'REJECTED', '2026-09-08 15:00:00', '2026-09-08 15:00:00', '2026-09-08 15:00:00');
END

IF NOT EXISTS (SELECT 1 FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv1_Id AND version_number = 2)
BEGIN
    INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
    VALUES (@Deliv1_Id, 2, @LeaderUserId, N'Đã hoàn thiện bổ sung use-case chi tiết theo nhận xét của thầy Nguyễn Hoàng Minh.', 'ACCEPTED', '2026-09-14 14:00:00', '2026-09-14 14:00:00', '2026-09-14 14:00:00');
END

-- Deliverable 2: Thiết kế kiến trúc & CSDL (M2)
DECLARE @Deliv2_Id bigint = (SELECT TOP 1 id FROM dbo.deliverables WHERE project_id = @ProjectId AND (milestone_id = @M2_Id OR title LIKE N'%Database%' OR title LIKE N'%Architecture%' OR title LIKE N'%Thiết kế%'));
IF @Deliv2_Id IS NULL
BEGIN
    INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at, evidence_scope)
    VALUES (@ProjectId, @M2_Id, N'Tài liệu Thiết kế Kiến trúc hệ thống, CSDL & API Specification', 
            N'Mô hình ERD chuẩn hóa, đặc tả RESTful API qua Swagger/OpenAPI 3.0 và sơ đồ triển khai hạ tầng Docker.',
            N'DESIGN_PACKAGE', '2026-10-15 17:00:00', 'ACCEPTED', @LeaderUserId, GETUTCDATE(), GETUTCDATE(), 'COMMON');
    SET @Deliv2_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

IF NOT EXISTS (SELECT 1 FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv2_Id AND version_number = 1)
BEGIN
    INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
    VALUES (@Deliv2_Id, 1, @LeaderUserId, N'Nộp gói tài liệu thiết kế CSDL và kiến trúc Clean Architecture hoàn chỉnh.', 'ACCEPTED', '2026-10-09 16:00:00', '2026-10-09 16:00:00', '2026-10-09 16:00:00');
END

-- Deliverable 3: Gói mã nguồn Core MVP & Bằng chứng kiểm thử (M3)
DECLARE @Deliv3_Id bigint = (SELECT TOP 1 id FROM dbo.deliverables WHERE project_id = @ProjectId AND (milestone_id = @M3_Id OR title LIKE N'%M3%' OR title LIKE N'%MVP%' OR title LIKE N'%mã nguồn%'));
IF @Deliv3_Id IS NULL
BEGIN
    INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at, evidence_scope)
    VALUES (@ProjectId, @M3_Id, N'Gói mã nguồn Core MVP & Bằng chứng kiểm thử tự động', 
            N'Source code backend .NET 8, frontend React, script migrate CSDL và bộ test cases tự động tích hợp CI/CD.',
            N'SOURCE_PACKAGE', '2026-11-28 17:00:00', 'SUBMITTED', @LeaderUserId, GETUTCDATE(), GETUTCDATE(), 'COMMON');
    SET @Deliv3_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

IF NOT EXISTS (SELECT 1 FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv3_Id AND version_number = 1)
BEGIN
    INSERT INTO dbo.deliverable_versions (deliverable_id, version_number, submitted_by, submission_note, status, submitted_at, created_at, updated_at)
    VALUES (@Deliv3_Id, 1, @LeaderUserId, N'Nộp sản phẩm đợt kiểm tra Milestone 3 kèm video demo các tính năng chính.', 'SUBMITTED', '2026-10-20 17:00:00', '2026-10-20 17:00:00', '2026-10-20 17:00:00');
END

-- Deliverable 4: Báo cáo tổng kết đồ án tốt nghiệp & Slide thuyết trình (M4)
DECLARE @Deliv4_Id bigint = (SELECT TOP 1 id FROM dbo.deliverables WHERE project_id = @ProjectId AND (milestone_id = @M4_Id OR title LIKE N'%Final%' OR title LIKE N'%Báo cáo tổng kết%'));
IF @Deliv4_Id IS NULL
BEGIN
    INSERT INTO dbo.deliverables (project_id, milestone_id, title, description, deliverable_type, due_at, status, created_by, created_at, updated_at, evidence_scope)
    VALUES (@ProjectId, @M4_Id, N'Báo cáo tổng kết đồ án tốt nghiệp & Slide thuyết trình bảo vệ', 
            N'Tài liệu báo cáo đồ án tốt nghiệp hoàn chỉnh theo mẫu FPT University và slide trình chiếu bảo vệ trước hội đồng.',
            N'FINAL_REPORT', '2026-12-25 17:00:00', 'OPEN', @LeaderUserId, GETUTCDATE(), GETUTCDATE(), 'COMMON');
    SET @Deliv4_Id = SCOPE_IDENTITY();
END

-- ==============================================================================================
-- 6. LỊCH HỌP & BIÊN BẢN HỌP (MEETINGS, ATTENDEES, DECISIONS, ACTION ITEMS)
-- ==============================================================================================
PRINT N'6. Thiết lập Lịch họp, Biên bản họp, Quyết định và Action items...';

DECLARE @Mtg1_Id bigint, @Mtg2_Id bigint, @Mtg3_Id bigint, @Mtg4_Id bigint;

-- Meeting 1: Khởi động dự án (COMPLETED, ONSITE)
SELECT TOP 1 @Mtg1_Id = id FROM dbo.meetings WHERE project_id = @ProjectId AND (CAST(start_at as date) = '2026-09-12' OR title LIKE N'%Khởi động%' OR title LIKE N'%Kickoff%');
IF @Mtg1_Id IS NULL
BEGIN
    INSERT INTO dbo.meetings (project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url, status, created_by, created_at, updated_at, concurrency_token, minutes, decisions, blockers, meeting_delivery_mode, video_channel, recurrence, notes_version)
    VALUES (@ProjectId, N'Họp khởi động dự án & Thống nhất đề cương với GVHD', 
            N'1. Giới thiệu các thành viên nhóm và phân công chuyên môn.\n2. Trình bày ý tưởng đề tài AI-PMS.\n3. Thống nhất phương thức trao đổi và lịch họp định kỳ hàng tuần.',
            N'Cuộc họp diễn ra thuận lợi, thầy Nguyễn Hoàng Minh đánh giá cao tính khả thi của đề tài.',
            '2026-09-12 14:00:00', '2026-09-12 15:30:00', N'Phòng Lab 302, Tòa nhà Gamma, ĐH FPT', NULL, 'COMPLETED',
            @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID(),
            N'Thầy Minh lưu ý nhóm cần làm rõ phạm vi module AI để tránh dàn trải. Thống nhất sử dụng Gitflow trong suốt quá trình phát triển.',
            N'- Chốt phạm vi đồ án tập trung vào quản lý tiến độ và cảnh báo trễ hạn thông minh.\n- Sử dụng .NET 8 Web API và React TypeScript.',
            N'Chưa có tài khoản Cloud FPT để cấu hình server thử nghiệm.',
            'ONSITE', 'NONE', 'NONE', 1);
    SET @Mtg1_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Meeting 2: Review kiến trúc CSDL & RBAC (COMPLETED, REMOTE)
SELECT TOP 1 @Mtg2_Id = id FROM dbo.meetings WHERE project_id = @ProjectId AND (CAST(start_at as date) = '2026-09-25' OR title LIKE N'%Database and Architecture%' OR title LIKE N'%Review%kiến trúc%');
IF @Mtg2_Id IS NULL
BEGIN
    INSERT INTO dbo.meetings (project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url, status, created_by, created_at, updated_at, concurrency_token, minutes, decisions, blockers, meeting_delivery_mode, video_channel, recurrence, notes_version)
    VALUES (@ProjectId, N'Review thiết kế kiến trúc CSDL & Luồng phân quyền RBAC', 
            N'1. Trình bày sơ đồ ERD 3NF chi tiết.\n2. Review các bảng phân quyền RBAC và token JWT.\n3. Thống nhất chuẩn code backend.',
            N'Góp ý từ GVHD về việc thêm unique index và soft delete cho các bảng quan trọng.',
            '2026-09-25 09:00:00', '2026-09-25 10:30:00', NULL, N'https://meet.google.com/aipms-sync-review', 'COMPLETED',
            @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID(),
            N'Nhóm đã tiếp thu toàn bộ góp ý về ERD, bổ sung thêm bảng audit log để theo dõi lịch sử thao tác.',
            N'- Đồng ý mô hình ERD với hơn 35 bảng.\n- Chấp thuận chuyển giao tài liệu thiết kế sang Sprint 2.',
            NULL, 'REMOTE', 'IN_APP_VIDEO', 'NONE', 1);
    SET @Mtg2_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Meeting 3: Họp Sprint 2: Tiến độ AI & Tháo gỡ Blocker (COMPLETED, HYBRID)
SELECT TOP 1 @Mtg3_Id = id FROM dbo.meetings WHERE project_id = @ProjectId AND (CAST(start_at as date) = '2026-10-18' OR title LIKE N'%M3 Weekly%' OR title LIKE N'%Sprint 2%');
IF @Mtg3_Id IS NULL
BEGIN
    INSERT INTO dbo.meetings (project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url, status, created_by, created_at, updated_at, concurrency_token, minutes, decisions, blockers, meeting_delivery_mode, video_channel, recurrence, notes_version)
    VALUES (@ProjectId, N'Họp Sprint 2: Đánh giá tiến độ tích hợp AI & Tháo gỡ Blocker', 
            N'1. Đánh giá kết quả Sprint 1 đã hoàn thành.\n2. Kiểm tra tiến độ Kanban board và các task đang bị nghẽn (SignalR, Google Drive API).\n3. Thử nghiệm demo module AI gợi ý tiến độ.',
            N'Nhóm đã tìm ra giải pháp cho CORS SignalR, đang chờ cấp quota Google Drive.',
            '2026-10-18 15:00:00', '2026-10-18 16:30:00', N'Phòng họp 204 Beta & Trực tuyến LiveKit', N'https://meet.aipms.fe.edu.vn/sprint2-sync', 'COMPLETED',
            @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID(),
            N'Bảo trình diễn mô hình phân tích tiến độ, độ chính xác đạt 92% trên dữ liệu đồ án các kỳ trước.',
            N'- Tạm thời dùng local storage cho file đính kèm trong khi chờ Google Drive API.\n- Ưu tiên hoàn thành tính năng nộp báo cáo tuần trước ngày 22/10.',
            N'Quota API OpenAI cần nạp thêm credit cho giai đoạn test mở rộng.',
            'HYBRID', 'IN_APP_VIDEO', 'NONE', 1);
    SET @Mtg3_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Meeting 4: Kế hoạch chuẩn bị Review Gate 2 & Đánh giá chéo (SCHEDULED, HYBRID)
SELECT TOP 1 @Mtg4_Id = id FROM dbo.meetings WHERE project_id = @ProjectId AND (CAST(start_at as date) = '2026-10-28' OR title LIKE N'%Final Submission Readiness%' OR title LIKE N'%Review Gate 2%');
IF @Mtg4_Id IS NULL
IF @Mtg4_Id IS NULL
BEGIN
    INSERT INTO dbo.meetings (project_id, title, agenda, meeting_notes, start_at, end_at, location, online_url, status, created_by, created_at, updated_at, concurrency_token, minutes, decisions, blockers, meeting_delivery_mode, video_channel, recurrence, notes_version)
    VALUES (@ProjectId, N'Kế hoạch chuẩn bị Review Gate 2 & Đánh giá chéo Peer Review', 
            N'1. Kiểm tra danh mục sản phẩm nghiệm thu Review Gate 2.\n2. Phân chia chuẩn bị slide và demo trực tiếp kịch bản người dùng.\n3. Hướng dẫn các thành viên thực hiện form đánh giá chéo đồng đội.',
            NULL, '2026-10-28 14:00:00', '2026-10-28 15:30:00', N'Phòng họp 101 Innovation & Google Meet', N'https://meet.google.com/aipms-gate2-prep', 'SCHEDULED',
            @LeaderUserId, GETUTCDATE(), GETUTCDATE(), NEWID(),
            NULL, NULL, NULL, 'HYBRID', 'EXTERNAL_LINK', 'NONE', 0);
    SET @Mtg4_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Thêm người tham gia cuộc họp (Meeting Participants)
DECLARE @AllMeetingIds TABLE (mtg_id bigint);
INSERT INTO @AllMeetingIds VALUES (@Mtg1_Id), (@Mtg2_Id), (@Mtg3_Id), (@Mtg4_Id);

INSERT INTO dbo.meeting_participants (meeting_id, user_id, attendance_status, rsvp_status, rsvp_token, created_at, updated_at)
SELECT m.mtg_id, u.id, 'ATTENDED', 'ACCEPTED', NEWID(), GETUTCDATE(), GETUTCDATE()
FROM @AllMeetingIds m
CROSS JOIN (
    SELECT id FROM dbo.users WHERE id IN (@User1, @User2, @User3, @User4, @User5, @SupervisorUserId)
) u
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.meeting_participants mp WHERE mp.meeting_id = m.mtg_id AND mp.user_id = u.id
);

-- Thêm Quyết định cuộc họp (Meeting Decisions)
IF NOT EXISTS (SELECT 1 FROM dbo.meeting_decisions WHERE meeting_id = @Mtg1_Id)
BEGIN
    INSERT INTO dbo.meeting_decisions (meeting_id, content, decided_by, decided_at, created_at)
    VALUES 
    (@Mtg1_Id, N'Thống nhất công nghệ: Frontend sử dụng React 18 TypeScript, Backend sử dụng ASP.NET Core 8 Web API và SQL Server 2022.', @SupervisorUserId, '2026-09-12 15:00:00', GETUTCDATE()),
    (@Mtg1_Id, N'Lịch họp định kỳ với GVHD chốt vào 14:00 chiều thứ Bảy hàng tuần (kết hợp Onsite và Remote).', @SupervisorUserId, '2026-09-12 15:15:00', GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM dbo.meeting_decisions WHERE meeting_id = @Mtg3_Id)
BEGIN
    INSERT INTO dbo.meeting_decisions (meeting_id, content, decided_by, decided_at, created_at)
    VALUES 
    (@Mtg3_Id, N'Tạm thời cô lập module SignalR trong feature-flag riêng để không ảnh hưởng đến demo nghiệm thu Sprint 2.', @LeaderUserId, '2026-10-18 16:00:00', GETUTCDATE()),
    (@Mtg3_Id, N'Tiến hành nộp gói sản phẩm Deliverable Milestone 3 trước 17:00 ngày 20/10/2026.', @LeaderUserId, '2026-10-18 16:15:00', GETUTCDATE());
END

-- Thêm Việc cần làm sau họp (Meeting Action Items)
IF NOT EXISTS (SELECT 1 FROM dbo.meeting_action_items WHERE meeting_id = @Mtg1_Id)
BEGIN
    INSERT INTO dbo.meeting_action_items (meeting_id, title, description, assignee_user_id, due_at, status, concurrency_token, created_by, created_at, updated_at)
    VALUES 
    (@Mtg1_Id, N'Hoàn thiện tài liệu Proposal v2.0 theo góp ý của thầy Minh', N'Chỉnh sửa mục tiêu nghiên cứu và bổ sung phạm vi chức năng AI', @User4, '2026-09-14 12:00:00', 'DONE', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    (@Mtg1_Id, N'Thiết lập repository GitHub tổ chức và cấu hình phân nhánh theo Gitflow', N'Tạo các branch develop, staging, main và phân quyền cho 5 thành viên', @User5, '2026-09-14 18:00:00', 'DONE', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
END

IF NOT EXISTS (SELECT 1 FROM dbo.meeting_action_items WHERE meeting_id = @Mtg3_Id)
BEGIN
    INSERT INTO dbo.meeting_action_items (meeting_id, title, description, assignee_user_id, due_at, status, concurrency_token, created_by, created_at, updated_at)
    VALUES 
    (@Mtg3_Id, N'Sửa lỗi CORS và kết nối Websocket cho SignalR Notification Hub', N'Kiểm tra lại cấu hình AllowCredentials và Origin whitelist trong Program.cs', @User2, '2026-10-23 17:00:00', 'IN_PROGRESS', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    (@Mtg3_Id, N'Hoàn thiện tài liệu mô tả thuật toán phân tích tiến độ AI', N'Viết tài liệu giải thuật Rule-Based Context Retriever để nộp kèm Deliverable M3', @User3, '2026-10-24 17:00:00', 'IN_PROGRESS', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE()),
    (@Mtg3_Id, N'Chuẩn bị kịch bản slide thuyết trình cho Review Gate 2', N'Tập trung vào các use-case đã chạy thực tế trên môi trường demo', @User1, '2026-10-27 17:00:00', 'OPEN', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
END

-- ==============================================================================================
-- 7. BÁO CÁO TIẾN ĐỘ CHU KỲ (PROGRESS REPORT PERIODS & REPORTS)
-- ==============================================================================================
PRINT N'7. Thiết lập Chu kỳ Báo cáo tiến độ và Nội dung báo cáo giàu định dạng...';

-- Chu kỳ báo cáo tuần 1-2 (Đã xong)
DECLARE @PeriodW1_Id bigint = (SELECT id FROM dbo.progress_report_periods WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-09-01 00:00:00');
IF @PeriodW1_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_report_periods (project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, @PeriodId, 'WEEKLY', '2026-09-01 00:00:00', '2026-09-07 23:59:59', '2026-09-08 23:59:59', 'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @PeriodW1_Id = SCOPE_IDENTITY();
END

-- Chu kỳ báo cáo tuần 2-3 (Đã xong)
DECLARE @PeriodW2_Id bigint = (SELECT id FROM dbo.progress_report_periods WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-09-08 00:00:00');
IF @PeriodW2_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_report_periods (project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, @PeriodId, 'WEEKLY', '2026-09-08 00:00:00', '2026-09-14 23:59:59', '2026-09-15 23:59:59', 'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @PeriodW2_Id = SCOPE_IDENTITY();
END

-- Chu kỳ báo cáo tháng 9 (Đã xong)
DECLARE @PeriodM1_Id bigint = (SELECT id FROM dbo.progress_report_periods WHERE project_id = @ProjectId AND report_type = 'MONTHLY' AND period_start = '2026-09-01 00:00:00');
IF @PeriodM1_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_report_periods (project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, @PeriodId, 'MONTHLY', '2026-09-01 00:00:00', '2026-09-30 23:59:59', '2026-10-02 23:59:59', 'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @PeriodM1_Id = SCOPE_IDENTITY();
END

-- Chu kỳ báo cáo tuần 6 (Đang diễn ra tháng 10)
DECLARE @PeriodW6_Id bigint = (SELECT id FROM dbo.progress_report_periods WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-10-15 00:00:00');
IF @PeriodW6_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_report_periods (project_id, project_period_id, report_type, period_start, period_end, deadline, late_policy, concurrency_token, created_by, created_at, updated_at)
    VALUES (@ProjectId, @PeriodId, 'WEEKLY', '2026-10-15 00:00:00', '2026-10-21 23:59:59', '2026-10-22 23:59:59', 'FLAG', NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @PeriodW6_Id = SCOPE_IDENTITY();
END

-- Cập nhật hoặc thêm Báo cáo tiến độ (Progress Reports)
-- Báo cáo 1: Tuần 1 (REVIEWED)
DECLARE @Rep1_Id bigint = (SELECT id FROM dbo.progress_reports WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-09-01');
IF @Rep1_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_reports (project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work, issues_and_risks, in_progress_work, blockers, risks, next_actions, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id, is_late)
    VALUES (@ProjectId, @LeaderUserId, 'WEEKLY', '2026-09-01', '2026-09-07', 
            N'Nhóm đã hoàn thành khảo sát quy trình Capstone thực tế và khởi tạo thiết kế ERD cơ sở dữ liệu trên SQL Server.',
            N'- Hoàn thành 100% tài liệu phỏng vấn quy trình nghiệp vụ.\n- Khởi tạo repo GitHub và cấu hình chuẩn coding convention.\n- Dự thảo sơ đồ ERD sơ bộ.',
            N'- Hoàn thiện chi tiết các bảng liên quan đến phân quyền và dự án.\n- Họp khởi động chính thức với GVHD.',
            N'Một số use-case về đánh giá chéo liên ngành chưa có tài liệu quy chuẩn từ phòng Đào tạo.',
            N'Đang hoàn thiện sơ đồ quan hệ các bảng Milestone và Deliverables.',
            NULL,
            N'Nguy cơ thay đổi quy chế nghiệm thu đồ án kỳ FA26.',
            N'Đặt lịch gặp trực tiếp thầy Minh vào chiều thứ Bảy để xin ý kiến chỉ đạo.',
            'REVIEWED', '2026-09-07 16:30:00', GETUTCDATE(), GETUTCDATE(), NEWID(), @PeriodW1_Id, 0);
    SET @Rep1_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Báo cáo 2: Tuần 2 (REVIEWED)
DECLARE @Rep2_Id bigint = (SELECT id FROM dbo.progress_reports WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-09-08');
IF @Rep2_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_reports (project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work, issues_and_risks, in_progress_work, blockers, risks, next_actions, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id, is_late)
    VALUES (@ProjectId, @LeaderUserId, 'WEEKLY', '2026-09-08', '2026-09-14', 
            N'Hoàn thiện toàn bộ đề cương chi tiết (Proposal v2) và bảo vệ thành công đề tài trước GVHD.',
            N'- Hoàn thành Proposal v2.0 và được thầy Minh phê duyệt.\n- Họp Kickoff ngày 12/09 thành công.\n- Chốt toàn bộ kiến trúc kỹ thuật của hệ thống.',
            N'- Bắt đầu Sprint 1: Xây dựng module xác thực và phân quyền RBAC.\n- Tạo khung backend Clean Architecture.',
            N'Không có trở ngại lớn.',
            N'Đang cấu hình ASP.NET Core Identity và token JWT.',
            NULL, NULL,
            N'Tiến hành phân chia công việc chi tiết cho các thành viên trong Sprint 1.',
            'REVIEWED', '2026-09-14 15:00:00', GETUTCDATE(), GETUTCDATE(), NEWID(), @PeriodW2_Id, 0);
    SET @Rep2_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Báo cáo 3: Tháng 9 (REVIEWED)
DECLARE @Rep3_Id bigint = (SELECT id FROM dbo.progress_reports WHERE project_id = @ProjectId AND report_type = 'MONTHLY' AND period_start = '2026-09-01' AND period_end = '2026-09-30');
IF @Rep3_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_reports (project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work, issues_and_risks, in_progress_work, blockers, risks, next_actions, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id, is_late)
    VALUES (@ProjectId, @LeaderUserId, 'MONTHLY', '2026-09-01', '2026-09-30', 
            N'Tổng kết tháng 9: Hoàn thành đúng tiến độ Milestone 1 (Proposal & Requirements) và hoàn thiện 70% Milestone 2 (Core MVP).',
            N'- Đề cương chính thức được duyệt.\n- Hoàn tất thiết kế CSDL 35 bảng.\n- Hoàn thành module Authentication & RBAC.\n- Hoàn thành giao diện Dashboard cơ bản.',
            N'- Đóng gói sản phẩm Milestone 2.\n- Bắt đầu triển khai Sprint 2 về Quản lý Nhiệm vụ và Tích hợp AI.',
            N'Cần tối ưu hóa hiệu năng câu truy vấn khi dữ liệu lớn.',
            N'Đang tiến hành code API tạo Sprint, Task và gán người thực hiện.',
            NULL, NULL,
            N'Tập trung cao độ cho Sprint 2 trong tháng 10.',
            'REVIEWED', '2026-09-30 18:00:00', GETUTCDATE(), GETUTCDATE(), NEWID(), @PeriodM1_Id, 0);
    SET @Rep3_Id = SCOPE_IDENTITY();
END
-- Giữ nguyên nếu đã tồn tại, không ghi đè khi rerun

-- Báo cáo 4: Tuần 6 (Tháng 10 - SUBMITTED, Chờ GVHD xem xét)
DECLARE @Rep4_Id bigint = (SELECT id FROM dbo.progress_reports WHERE project_id = @ProjectId AND report_type = 'WEEKLY' AND period_start = '2026-10-15');
IF @Rep4_Id IS NULL
BEGIN
    INSERT INTO dbo.progress_reports (project_id, submitted_by, report_type, period_start, period_end, summary, completed_work, planned_work, issues_and_risks, in_progress_work, blockers, risks, next_actions, status, submitted_at, created_at, updated_at, concurrency_token, progress_report_period_id, is_late)
    VALUES (@ProjectId, @LeaderUserId, 'WEEKLY', '2026-10-15', '2026-10-21', 
            N'Báo cáo tiến độ tuần 6: Nhóm đạt 85% khối lượng công việc của Sprint 2, đã tích hợp thành công mô hình AI Rule-Based và bảng Kanban.',
            N'- Hoàn thành giao diện Kanban tương tác kéo thả mượt mà.\n- Tích hợp thành công thuật toán phân tích tiến độ đồ án đạt độ chính xác 92%.\n- Đã nộp gói Deliverable Milestone 3 lên hệ thống.',
            N'- Tháo gỡ hoàn toàn lỗi kết nối SignalR realtime.\n- Chuẩn bị slide và kịch bản demo cho buổi Review Gate 2 sắp tới.',
            N'Đang chờ Google Cloud Console kích hoạt hạn ngạch OAuth Drive API.',
            N'Đang test chức năng nộp nhiều phiên bản của Deliverable và phân quyền tải file.',
            N'Chưa có tài khoản Cloud chính thức để lưu trữ file dung lượng lớn.',
            N'Rủi ro nghẽn mạng khi demo trực tiếp tính năng họp video LiveKit.',
            N'Đầu tuần tới nhóm sẽ họp thử demo nội bộ để rà soát lỗi giao diện.',
            'SUBMITTED', '2026-10-21 16:45:00', GETUTCDATE(), GETUTCDATE(), NEWID(), @PeriodW6_Id, 0);
    SET @Rep4_Id = SCOPE_IDENTITY();
END

-- ==============================================================================================
-- 8. PHẢN HỒI CỦA GIẢNG VIÊN HƯỚNG DẪN (SUPERVISOR FEEDBACK)
-- ==============================================================================================
PRINT N'8. Thêm các nhận xét đánh giá của Giảng viên hướng dẫn...';

-- Feedback cho Deliverable 1 v2 (Đề cương SRS)
DECLARE @Deliv1_v2 bigint = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv1_Id AND version_number = 2);
IF @Deliv1_v2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_feedback WHERE deliverable_version_id = @Deliv1_v2)
BEGIN
    INSERT INTO dbo.supervisor_feedback (project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, feedback_text, created_at, updated_at, gate_code)
    VALUES (@ProjectId, @SupervisorAssignmentId, NULL, @Deliv1_v2, NULL, 
            N'Đề cương được hoàn thiện rất chi tiết, các use-case nghiệp vụ phân định rõ ràng quyền của từng bên. Thầy đồng ý phê duyệt để nhóm bước vào giai đoạn thiết kế kiến trúc và CSDL.',
            '2026-09-14 16:00:00', '2026-09-14 16:00:00', 'GATE-01');
END

-- Feedback cho Deliverable 2 v1 (CSDL & Architecture)
DECLARE @Deliv2_v1 bigint = (SELECT id FROM dbo.deliverable_versions WHERE deliverable_id = @Deliv2_Id AND version_number = 1);
IF @Deliv2_v1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_feedback WHERE deliverable_version_id = @Deliv2_v1)
BEGIN
    INSERT INTO dbo.supervisor_feedback (project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, feedback_text, created_at, updated_at, gate_code)
    VALUES (@ProjectId, @SupervisorAssignmentId, NULL, @Deliv2_v1, NULL, 
            N'Mô hình CSDL chuẩn 3NF tốt, các ràng buộc check constraints và foreign key được cài đặt đầy đủ. Lưu ý khi triển khai API cần cache các bảng danh mục như Major, Department để tối ưu hiệu năng.',
            '2026-10-10 10:30:00', '2026-10-10 10:30:00', 'GATE-02');
END

-- Feedback cho Báo cáo tiến độ tuần 1
IF @Rep1_Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_feedback WHERE progress_report_id = @Rep1_Id)
BEGIN
    INSERT INTO dbo.supervisor_feedback (project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, feedback_text, created_at, updated_at, gate_code)
    VALUES (@ProjectId, @SupervisorAssignmentId, @Rep1_Id, NULL, NULL, 
            N'Khởi đầu tốt. Nhóm làm việc tích cực và phân công trách nhiệm rõ ràng. Cần duy trì nhịp độ làm việc này trong các tuần tiếp theo.',
            '2026-09-08 09:00:00', '2026-09-08 09:00:00', NULL);
END

-- Feedback cho Báo cáo tiến độ tuần 2
IF @Rep2_Id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_feedback WHERE progress_report_id = @Rep2_Id)
BEGIN
    INSERT INTO dbo.supervisor_feedback (project_id, supervisor_assignment_id, progress_report_id, deliverable_version_id, meeting_id, feedback_text, created_at, updated_at, gate_code)
    VALUES (@ProjectId, @SupervisorAssignmentId, @Rep2_Id, NULL, NULL, 
            N'Đã tiếp thu và khắc phục triệt để các nhận xét từ buổi họp Kickoff. Duyệt đề cương và cho phép nhóm bắt đầu Sprint 1.',
            '2026-09-15 08:30:00', '2026-09-15 08:30:00', NULL);
END

-- ==============================================================================================
-- 9. THỎA ƯỚC LÀM VIỆC NHÓM (WORKING AGREEMENT)
-- ==============================================================================================
PRINT N'9. Thiết lập Thỏa ước làm việc nhóm (Working Agreement) & Ký duyệt...';

DECLARE @AgreementId bigint = (SELECT id FROM dbo.project_working_agreements WHERE project_id = @ProjectId AND version = 1);
DECLARE @AgreementJson nvarchar(max) = N'[
  {
    "title": "1. Nguyên tắc Giao tiếp & Tương tác nhóm",
    "body": "- Mọi thành viên cam kết theo dõi kênh chat dự án ít nhất 2 lần/ngày.\n- Phản hồi tin nhắn và email công việc trong vòng tối đa 4 tiếng vào ban ngày.\n- Giữ thái độ tôn trọng, lắng nghe và xây dựng trong mọi buổi thảo luận."
  },
  {
    "title": "2. Quy chuẩn Gitflow & Review Mã nguồn (Code Review)",
    "body": "- Nghiêm cấm push mã nguồn trực tiếp lên nhánh main và develop.\n- Đặt tên branch theo định dạng: feat/task-code, fix/task-code, refactor/task-code.\n- Mỗi Pull Request phải có ít nhất 1 thành viên review và phê duyệt trước khi được merge.\n- Tất cả commit message phải tuân thủ chuẩn Conventional Commits."
  },
  {
    "title": "3. Cam kết Deadline & Giải quyết Xung đột",
    "body": "- Nộp kết quả công việc trước deadline được giao tối thiểu 6 tiếng để team tích hợp.\n- Khi gặp khó khăn kỹ thuật quá 24 tiếng (Blocker), bắt buộc phải báo ngay cho Tech Lead hoặc GVHD để được hỗ trợ.\n- Xử lý mâu thuẫn bằng hình thức biểu quyết đa số. Trường hợp ngang phiếu, quyết định cuối cùng thuộc về Trưởng nhóm."
  },
  {
    "title": "4. Kỷ luật Họp & Báo cáo tiến độ",
    "body": "- Tham gia đầy đủ các buổi họp định kỳ với GVHD đúng giờ. Vắng họp phải báo trước ít nhất 12 tiếng kèm lý do chính đáng.\n- Hoàn thành đầy đủ nội dung đóng góp trong báo cáo tiến độ tuần trước 17:00 Chủ nhật."
  }
]';

-- [WORKING AGREEMENT GOVERNANCE & INTEGRITY GUARD]
-- Rerun giữ nguyên nội dung, version và acceptance đã có; KHÔNG ghi đè cam kết đã công bố hoặc làm sống lại version superseded.
IF @AgreementId IS NULL
BEGIN
    INSERT INTO dbo.project_working_agreements (project_id, version, status, content_json, concurrency_token, created_by, created_at, published_at)
    VALUES (@ProjectId, 1, 'ACTIVE', @AgreementJson, NEWID(), @LeaderUserId, GETUTCDATE(), GETUTCDATE());
    SET @AgreementId = SCOPE_IDENTITY();

    -- Chỉ tạo liên kết thành viên ban đầu khi tạo mới Working Agreement demo
    INSERT INTO dbo.agreement_members (agreement_id, user_id, team_member_id, membership_joined_at, membership_generation, accepted_at)
    SELECT @AgreementId, m.user_id, m.team_member_id, GETUTCDATE(), m.membership_generation, GETUTCDATE()
    FROM @Members m
    WHERE NOT EXISTS (
        SELECT 1 FROM dbo.agreement_members am 
        WHERE am.agreement_id = @AgreementId AND am.user_id = m.user_id
    );
END
ELSE
BEGIN
    PRINT N'-> Working Agreement đã tồn tại: giữ nguyên cam kết/version/lịch sử xác nhận hiện có, không ghi đè.';
END

-- ==============================================================================================
-- 10. FILE MINH CHỨNG MÔ PHỎNG (ATTACHED FILES - MOCK METADATA CHO UI/DEMO)
-- ==============================================================================================
-- LƯU Ý / GHI CHÚ QUAN TRỌNG:
-- Các bản ghi file dưới đây là Metadata mô phỏng (Mock File Records) phục vụ hiển thị trực quan 
-- trên giao diện (UI) danh sách deliverables, dung lượng và định dạng tập tin. 
-- Script không chứa binary content thật trên S3/MinIO/Blob Storage. Để test tải file end-to-end,
-- vui lòng sử dụng tính năng Upload File thực tế trên Web Portal hoặc dùng file test chuyên dụng.
-- ==============================================================================================
PRINT N'10. Bổ sung các file đính kèm mô phỏng cho Deliverables (mock metadata cho UI)...';

IF @Deliv1_v2 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.files WHERE deliverable_version_id = @Deliv1_v2)
BEGIN
    INSERT INTO dbo.files (uploaded_by, deliverable_version_id, original_file_name, stored_file_name, storage_path, file_url, mime_type, file_size_bytes, checksum_sha256, created_at, updated_at)
    VALUES (@LeaderUserId, @Deliv1_v2, N'AIPMS_Software_Requirements_Specification_v2.0.pdf', 
            N'aipms_srs_v2_f8d2.pdf', N'/uploads/deliverables/aipms_srs_v2_f8d2.pdf', 
            N'https://storage.aipms.fe.edu.vn/deliverables/aipms_srs_v2_f8d2.pdf',
            N'application/pdf', 3450200, N'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855', GETUTCDATE(), GETUTCDATE());
END

IF @Deliv2_v1 IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.files WHERE deliverable_version_id = @Deliv2_v1)
BEGIN
    INSERT INTO dbo.files (uploaded_by, deliverable_version_id, original_file_name, stored_file_name, storage_path, file_url, mime_type, file_size_bytes, checksum_sha256, created_at, updated_at)
    VALUES (@LeaderUserId, @Deliv2_v1, N'AIPMS_Database_Architecture_Design_Package.zip', 
            N'aipms_db_arch_a7e1.zip', N'/uploads/deliverables/aipms_db_arch_a7e1.zip', 
            N'https://storage.aipms.fe.edu.vn/deliverables/aipms_db_arch_a7e1.zip',
            N'application/zip', 7840120, N'bca0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b899', GETUTCDATE(), GETUTCDATE());
END

COMMIT TRANSACTION;

PRINT N'======================================================================================';
PRINT N'ĐÃ HOÀN TẤT SEED DỮ LIỆU VISUAL DEMO THÀNH CÔNG CHO DỰ ÁN: ' + @TargetProjectCode;
PRINT N'======================================================================================';

-- ==============================================================================================
-- 11. BÁO CÁO THỐNG KÊ DỮ LIỆU HIỆN HỮU CỦA DỰ ÁN
-- ==============================================================================================
SELECT 'Milestones (Giai đoạn)' AS [Danh mục], COUNT(*) AS [Số lượng bản ghi] FROM dbo.milestones WHERE project_id = @ProjectId
UNION ALL
SELECT 'Sprints (Chu kỳ phát triển Agile)', COUNT(*) FROM dbo.sprints WHERE project_id = @ProjectId
UNION ALL
SELECT 'Tasks (Nhiệm vụ Kanban)', COUNT(*) FROM dbo.tasks WHERE sprint_id IN (SELECT id FROM dbo.sprints WHERE project_id = @ProjectId) OR milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId)
UNION ALL
SELECT 'Task Assignees (Phân công thành viên)', COUNT(*) FROM dbo.task_assignees WHERE task_id IN (SELECT id FROM dbo.tasks WHERE milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId))
UNION ALL
SELECT 'Deliverables (Sản phẩm bàn giao)', COUNT(*) FROM dbo.deliverables WHERE project_id = @ProjectId
UNION ALL
SELECT 'Deliverable Versions (Phiên bản nộp bài)', COUNT(*) FROM dbo.deliverable_versions WHERE deliverable_id IN (SELECT id FROM dbo.deliverables WHERE project_id = @ProjectId)
UNION ALL
SELECT 'Meetings (Cuộc họp & Biên bản)', COUNT(*) FROM dbo.meetings WHERE project_id = @ProjectId
UNION ALL
SELECT 'Meeting Participants (Người tham dự họp)', COUNT(*) FROM dbo.meeting_participants WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId)
UNION ALL
SELECT 'Meeting Decisions (Quyết định sau họp)', COUNT(*) FROM dbo.meeting_decisions WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId)
UNION ALL
SELECT 'Meeting Action Items (Việc cần làm sau họp)', COUNT(*) FROM dbo.meeting_action_items WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId)
UNION ALL
SELECT 'Progress Report Periods (Chu kỳ báo cáo)', COUNT(*) FROM dbo.progress_report_periods WHERE project_id = @ProjectId
UNION ALL
SELECT 'Progress Reports (Báo cáo tiến độ tuần/tháng)', COUNT(*) FROM dbo.progress_reports WHERE project_id = @ProjectId
UNION ALL
SELECT 'Supervisor Feedbacks (Nhận xét của GVHD)', COUNT(*) FROM dbo.supervisor_feedback WHERE project_id = @ProjectId
UNION ALL
SELECT 'Working Agreements (Thỏa ước nhóm)', COUNT(*) FROM dbo.project_working_agreements WHERE project_id = @ProjectId
UNION ALL
SELECT 'Agreement Signatures (Chữ ký thành viên)', COUNT(*) FROM dbo.agreement_members WHERE agreement_id IN (SELECT id FROM dbo.project_working_agreements WHERE project_id = @ProjectId);
