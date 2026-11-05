SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

DECLARE @password nvarchar(500) = N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg==';
DECLARE @org bigint = 1;

BEGIN TRANSACTION;

-- 1. Ensure Roles
DECLARE @roleAdmin bigint = (SELECT id FROM dbo.roles WHERE code = N'ADMIN');
DECLARE @roleStaff bigint = (SELECT id FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF');
DECLARE @roleLecturer bigint = (SELECT id FROM dbo.roles WHERE code = N'LECTURER');
DECLARE @roleStudent bigint = (SELECT id FROM dbo.roles WHERE code = N'STUDENT');

-- 2. Add New Realistic Users
DECLARE @newUsers TABLE (
    email nvarchar(255),
    full_name nvarchar(200),
    department_id bigint,
    major_id bigint,
    role_code nvarchar(50),
    student_code nvarchar(50),
    employee_code nvarchar(50)
);

INSERT @newUsers VALUES
-- Admin
(N'admin.secondary@fpt.edu.vn', N'Hoàng Nam Phương', NULL, NULL, N'ADMIN', NULL, N'EMP-ADM02'),
-- Department Staff
(N'staff.it02@fpt.edu.vn', N'Đặng Tuấn Anh', 1, NULL, N'DEPARTMENT_STAFF', NULL, N'EMP-IT02'),
(N'staff.ba01@fpt.edu.vn', N'Võ Thị Mai Lan', 2, NULL, N'DEPARTMENT_STAFF', NULL, N'EMP-BA01'),
-- Lecturers / Evaluators
(N'lecturer.se02@fe.edu.vn', N'Đỗ Quang Huy', 1, NULL, N'LECTURER', NULL, N'EMP-SE02'),
(N'lecturer.ai02@fe.edu.vn', N'Vũ Mai Hương', 1, NULL, N'LECTURER', NULL, N'EMP-AI02'),
(N'lecturer.mkt01@fe.edu.vn', N'Bùi Quỳnh Nga', 2, NULL, N'LECTURER', NULL, N'EMP-MKT01'),
-- Students Team 2 (Smart Logistics)
(N'leader.team2@fpt.edu.vn', N'Nguyễn Văn An', 1, 1, N'STUDENT', N'SE170001', NULL),
(N'member1.team2@fpt.edu.vn', N'Lê Thị Bích', 1, 2, N'STUDENT', N'AI170002', NULL),
(N'member2.team2@fpt.edu.vn', N'Trần Quốc Cường', 1, 3, N'STUDENT', N'DS170003', NULL),
-- Students Team 3 (FinTech Microservices)
(N'leader.team3@fpt.edu.vn', N'Phạm Hoàng Dũng', 1, 1, N'STUDENT', N'SE170004', NULL),
(N'member1.team3@fpt.edu.vn', N'Đỗ Minh Hạnh', 2, 5, N'STUDENT', N'BA170005', NULL);

-- Insert or update users
MERGE dbo.users AS target
USING @newUsers AS source
ON target.email = source.email
WHEN MATCHED THEN
    UPDATE SET password_hash = @password, full_name = source.full_name, status = 'ACTIVE', academic_profile_status = 'VERIFIED'
WHEN NOT MATCHED THEN
    INSERT (department_id, major_id, email, password_hash, full_name, student_code, employee_code, status, academic_profile_status)
    VALUES (source.department_id, source.major_id, source.email, @password, source.full_name, source.student_code, source.employee_code, 'ACTIVE', 'VERIFIED');

-- Update ALL existing demo users to have the unified password
UPDATE dbo.users SET password_hash = @password, status = 'ACTIVE' WHERE email LIKE '%@fpt.edu.vn' OR email LIKE '%@fe.edu.vn';

-- Assign User Roles
INSERT dbo.user_roles (user_id, role_id)
SELECT u.id, r.id
FROM @newUsers nu
JOIN dbo.users u ON u.email = nu.email
JOIN dbo.roles r ON r.code = nu.role_code
WHERE NOT EXISTS (SELECT 1 FROM dbo.user_roles ur WHERE ur.user_id = u.id AND ur.role_id = r.id);

-- Add Supervisor Profiles for all lecturers
INSERT dbo.supervisor_profiles (user_id, max_active_projects, is_available, created_at, updated_at)
SELECT u.id, 10, 1, GETUTCDATE(), GETUTCDATE()
FROM dbo.users u
JOIN dbo.user_roles ur ON u.id = ur.user_id
JOIN dbo.roles r ON ur.role_id = r.id
WHERE r.code = N'LECTURER'
AND NOT EXISTS (SELECT 1 FROM dbo.supervisor_profiles sp WHERE sp.user_id = u.id);

-- 3. Additional Teams
IF NOT EXISTS (SELECT 1 FROM dbo.teams WHERE code = N'TEAM-FA26-002')
BEGIN
    INSERT INTO dbo.teams (academic_semester_id, code, name, description, status, created_by, created_at, updated_at)
    VALUES (1, N'TEAM-FA26-002', N'Smart Logistics Solutions', N'Đồ án tối ưu hóa chuỗi cung ứng bằng AI', N'ELIGIBLE', (SELECT id FROM dbo.users WHERE email = N'leader.team2@fpt.edu.vn'), GETUTCDATE(), GETUTCDATE());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.teams WHERE code = N'TEAM-FA26-003')
BEGIN
    INSERT INTO dbo.teams (academic_semester_id, code, name, description, status, created_by, created_at, updated_at)
    VALUES (1, N'TEAM-FA26-003', N'FinTech Innovation Lab', N'Nền tảng thanh toán vi mô và chống gian lận', N'ELIGIBLE', (SELECT id FROM dbo.users WHERE email = N'leader.team3@fpt.edu.vn'), GETUTCDATE(), GETUTCDATE());
END;

DECLARE @team2Id bigint = (SELECT id FROM dbo.teams WHERE code = N'TEAM-FA26-002');
DECLARE @team3Id bigint = (SELECT id FROM dbo.teams WHERE code = N'TEAM-FA26-003');

-- Team members for Team 2
IF @team2Id IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @team2Id AND user_id = (SELECT id FROM dbo.users WHERE email = N'leader.team2@fpt.edu.vn'))
        INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, created_at, updated_at, membership_generation)
        VALUES (@team2Id, 1, (SELECT id FROM dbo.users WHERE email = N'leader.team2@fpt.edu.vn'), 1, GETUTCDATE(), GETUTCDATE(), GETUTCDATE(), NEWID());

    IF NOT EXISTS (SELECT 1 FROM dbo.team_members WHERE team_id = @team2Id AND user_id = (SELECT id FROM dbo.users WHERE email = N'member1.team2@fpt.edu.vn'))
        INSERT INTO dbo.team_members (team_id, academic_semester_id, user_id, is_leader, joined_at, created_at, updated_at, membership_generation)
        VALUES (@team2Id, 1, (SELECT id FROM dbo.users WHERE email = N'member1.team2@fpt.edu.vn'), 0, GETUTCDATE(), GETUTCDATE(), GETUTCDATE(), NEWID());
END;

-- 4. Additional Projects in different stages
IF NOT EXISTS (SELECT 1 FROM dbo.projects WHERE code = N'SEP490-FA26-SLOG')
BEGIN
    INSERT INTO dbo.projects (team_id, code, title, description, objectives, status, registered_at, submitted_at, approved_at, created_by, created_at, updated_at, proposal_source, milestones_initialized)
    VALUES (@team2Id, N'SEP490-FA26-SLOG', N'Smart Logistics Fleet Optimization Platform', N'Nền tảng tối ưu hóa lộ trình xe tải và quản lý kho thông minh', N'Tối ưu quãng đường di chuyển và quản lý tồn kho thời gian thực', N'ACTIVE', '2026-09-05 08:00:00', '2026-09-10 10:00:00', '2026-09-15 14:00:00', (SELECT id FROM dbo.users WHERE email = N'leader.team2@fpt.edu.vn'), GETUTCDATE(), GETUTCDATE(), 'STUDENT_PROPOSAL', 1);
    
    DECLARE @p2Id bigint = SCOPE_IDENTITY();
    INSERT INTO dbo.project_majors (project_id, major_id, created_at) VALUES (@p2Id, 1, GETUTCDATE()), (@p2Id, 2, GETUTCDATE()), (@p2Id, 3, GETUTCDATE());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.projects WHERE code = N'SEP490-FA26-FIN')
BEGIN
    INSERT INTO dbo.projects (team_id, code, title, description, objectives, status, registered_at, submitted_at, created_by, created_at, updated_at, proposal_source, milestones_initialized)
    VALUES (@team3Id, N'SEP490-FA26-FIN', N'FinTech Microservices Core Banking & Fraud Detection', N'Kiến trúc ngân hàng số phân tán tích hợp phát hiện gian lận thời gian thực', N'Xây dựng hệ thống giao dịch chịu tải cao và cảnh báo rủi ro tức thì', N'UNDER_REVIEW', '2026-09-20 09:00:00', '2026-09-25 11:00:00', (SELECT id FROM dbo.users WHERE email = N'leader.team3@fpt.edu.vn'), GETUTCDATE(), GETUTCDATE(), 'PUBLISHED_TOPIC', 0);

    DECLARE @p3Id bigint = SCOPE_IDENTITY();
    INSERT INTO dbo.project_majors (project_id, major_id, created_at) VALUES (@p3Id, 1, GETUTCDATE()), (@p3Id, 5, GETUTCDATE());
END;

-- 5. Set Project #1 specifically for Evaluation Demo
UPDATE dbo.projects SET status = 'FINAL_SUBMISSION' WHERE id = 1;

-- Ensure Assignment 1 (evaluator 2: lecturer.se01) and Assignment 2 (evaluator 3: lecturer.ai01) are both ACTIVE
UPDATE dbo.evaluation_assignments 
SET evaluator_id = 2, status = 'ACTIVE', revoked_at = NULL, revocation_reason = NULL 
WHERE id = 1;

UPDATE dbo.evaluation_assignments 
SET evaluator_id = 3, status = 'ACTIVE', revoked_at = NULL, revocation_reason = NULL 
WHERE id = 2;

COMMIT TRANSACTION;

SELECT 'DATABASE PREPARATION AND SEEDING COMPLETED SUCCESSFULLY' AS Result;
