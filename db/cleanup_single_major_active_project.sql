/* ==============================================================================================
   SCRIPT: cleanup_single_major_active_project.sql
   MỤC ĐÍCH: Kịch bản gỡ bỏ/thu hồi (Rollback/Cleanup) an toàn cho đồ án ĐƠN NGÀNH SE
   ĐỊNH DANH SEED: DEMO-SINGLE-MAJOR-ACTIVE-V1
   MÃ DỰ ÁN: SEP490-FA26-SE01 | MÃ NHÓM: SEP490_G03
   HỆ THỐNG: AI-PMS (AI Project Management System) - FPT University Da Nang
   
   AN TOÀN TUYỆT ĐỐI (SURGICAL CLEANUP):
     - CHỈ XÓA ĐÚNG DỮ LIỆU ĐƯỢC TẠO BỞI BỘ SEED 'SEP490-FA26-SE01'.
     - TUYỆT ĐỐI KHÔNG XÓA bảng, KHÔNG TRUNCATE và KHÔNG CHẠM đến Project 1 & Project 2.
     - Xóa tuần tự theo thứ tự khoá ngoại (Foreign Key) phụ thuộc từ bảng con lên bảng cha.
   ============================================================================================== */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

PRINT N'======================================================================================';
PRINT N'BẮT ĐẦU CLEANUP AN TOÀN CHO DỮ LIỆU SEED: SEP490-FA26-SE01';
PRINT N'======================================================================================';

DECLARE @ProjectCode nvarchar(50) = N'SEP490-FA26-SE01';
DECLARE @TeamCode nvarchar(50) = N'SEP490_G03';

DECLARE @ProjectId bigint = (SELECT id FROM dbo.projects WHERE code = @ProjectCode);
DECLARE @TeamId bigint = (SELECT id FROM dbo.teams WHERE code = @TeamCode);

IF @ProjectId IS NULL AND @TeamId IS NULL
BEGIN
    PRINT N'[THÔNG BÁO]: Không tìm thấy dự án hoặc nhóm cần dọn dẹp. Database đã sạch sẽ!';
    RETURN;
END

BEGIN TRANSACTION;

-- Lấy danh sách ID sinh viên demo thuộc nhóm SE01
DECLARE @StudentUserIds TABLE (user_id bigint);
INSERT INTO @StudentUserIds (user_id)
SELECT id FROM dbo.users WHERE student_code IN (N'SE180201', N'SE180202', N'SE180203', N'SE180204');

IF @ProjectId IS NOT NULL
BEGIN
    PRINT N'1. Xóa dữ liệu phụ thuộc của Đồ án ID: ' + CAST(@ProjectId AS nvarchar(10)) + N'...';

    -- 1.1. Files & Deliverables
    DELETE FROM dbo.files WHERE deliverable_version_id IN (
        SELECT dv.id FROM dbo.deliverable_versions dv
        JOIN dbo.deliverables d ON dv.deliverable_id = d.id
        WHERE d.project_id = @ProjectId
    );

    DELETE FROM dbo.deliverable_versions WHERE deliverable_id IN (
        SELECT id FROM dbo.deliverables WHERE project_id = @ProjectId
    );

    DELETE FROM dbo.deliverables WHERE project_id = @ProjectId;

    -- 1.2. Supervisor Feedback & Progress Reports
    DELETE FROM dbo.supervisor_feedback WHERE project_id = @ProjectId;
    DELETE FROM dbo.progress_reports WHERE project_id = @ProjectId;
    DELETE FROM dbo.progress_report_periods WHERE project_id = @ProjectId;

    -- 1.3. Meetings, Participants, Decisions & Action Items
    DELETE FROM dbo.meeting_participants WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId);
    DELETE FROM dbo.meeting_decisions WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId);
    DELETE FROM dbo.meeting_action_items WHERE meeting_id IN (SELECT id FROM dbo.meetings WHERE project_id = @ProjectId);
    DELETE FROM dbo.meetings WHERE project_id = @ProjectId;

    -- 1.4. Tasks, Assignees, Labels & Status History
    DELETE FROM dbo.task_status_history WHERE task_id IN (
        SELECT t.id FROM dbo.tasks t
        JOIN dbo.milestones m ON t.milestone_id = m.id
        WHERE m.project_id = @ProjectId
    );

    DELETE FROM dbo.task_board_labels WHERE task_id IN (
        SELECT t.id FROM dbo.tasks t
        JOIN dbo.milestones m ON t.milestone_id = m.id
        WHERE m.project_id = @ProjectId
    );

    DELETE FROM dbo.task_assignees WHERE task_id IN (
        SELECT t.id FROM dbo.tasks t
        JOIN dbo.milestones m ON t.milestone_id = m.id
        WHERE m.project_id = @ProjectId
    );

    DELETE FROM dbo.tasks WHERE milestone_id IN (SELECT id FROM dbo.milestones WHERE project_id = @ProjectId);

    -- 1.5. Sprints & Milestones
    DELETE FROM dbo.sprints WHERE project_id = @ProjectId;
    DELETE FROM dbo.milestones WHERE project_id = @ProjectId;

    -- 1.6. Working Agreements & Signatures
    DELETE FROM dbo.agreement_members WHERE agreement_id IN (
        SELECT id FROM dbo.project_working_agreements WHERE project_id = @ProjectId
    );
    DELETE FROM dbo.project_working_agreements WHERE project_id = @ProjectId;

    -- 1.7. Supervisor Assignments & Requests
    DELETE FROM dbo.supervisor_assignments WHERE project_id = @ProjectId;
    DELETE FROM dbo.supervisor_requests WHERE project_id = @ProjectId;

    -- 1.8. Project History, Majors & Requirements
    DELETE FROM dbo.project_status_history WHERE project_id = @ProjectId;
    DELETE FROM dbo.project_major_requirements WHERE project_id = @ProjectId;
    DELETE FROM dbo.project_majors WHERE project_id = @ProjectId;
    DELETE FROM dbo.project_tags WHERE project_id = @ProjectId;

    -- 1.9. Xóa Project chính
    DELETE FROM dbo.projects WHERE id = @ProjectId;
    PRINT N'-> Đã xóa thành công Project: ' + @ProjectCode;
END

IF @TeamId IS NOT NULL
BEGIN
    PRINT N'2. Xóa dữ liệu Nhóm ID: ' + CAST(@TeamId AS nvarchar(10)) + N'...';

    DELETE FROM dbo.team_academic_configurations WHERE team_id = @TeamId;
    DELETE FROM dbo.team_major_requirements WHERE team_id = @TeamId;
    DELETE FROM dbo.team_members WHERE team_id = @TeamId;
    DELETE FROM dbo.teams WHERE id = @TeamId;
    PRINT N'-> Đã xóa thành công Team: ' + @TeamCode;
END

-- 3. Xóa các tài khoản sinh viên demo tổng hợp (chỉ xóa tài khoản tạo cho SE01)
PRINT N'3. Dọn dẹp tài khoản sinh viên Demo (SE180201 - SE180204)...';
DELETE FROM dbo.user_roles WHERE user_id IN (SELECT user_id FROM @StudentUserIds);
DELETE FROM dbo.users WHERE id IN (SELECT user_id FROM @StudentUserIds);

COMMIT TRANSACTION;

PRINT N'======================================================================================';
PRINT N'CLEANUP THÀNH CÔNG! TOÀN BỘ DỮ LIỆU CỦA BỘ SEED ĐÃ ĐƯỢC THU HỒI AN TOÀN.';
PRINT N'CÁC DỰ ÁN VÀ NGƯỜI DÙNG KHÁC HOÀN TOÀN KHÔNG BỊ ẢNH HƯỞNG.';
PRINT N'======================================================================================';
