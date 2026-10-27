SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- 1. Ensure Organization exists
DECLARE @org_id BIGINT = (SELECT TOP 1 id FROM dbo.organizations ORDER BY id);
IF @org_id IS NULL
BEGIN
    INSERT INTO dbo.organizations (code, name, description, is_active)
    VALUES (N'FPTU', N'Trường Đại học FPT', N'Tổ chức giáo dục FPT', 1);
    SET @org_id = SCOPE_IDENTITY();
END;

-- 2. Add Department DESIGN if not exists
IF NOT EXISTS (SELECT 1 FROM dbo.departments WHERE code = N'DESIGN')
BEGIN
    INSERT INTO dbo.departments (organization_id, code, name, description, is_active)
    VALUES (@org_id, N'DESIGN', N'Bộ môn Thiết kế Đồ họa (Design)', N'Khoa Thiết kế Đồ họa và Mỹ thuật Đa phương tiện', 1);
END;

UPDATE dbo.departments SET name = N'Bộ môn Thiết kế Đồ họa (Design)', description = N'Khoa Thiết kế Đồ họa và Mỹ thuật Đa phương tiện' WHERE code = N'DESIGN';

DECLARE @design_dept_id BIGINT = (SELECT id FROM dbo.departments WHERE code = N'DESIGN');

-- 3. Add Major GD (Graphic Design) if not exists
IF NOT EXISTS (SELECT 1 FROM dbo.majors WHERE department_id = @design_dept_id AND code = N'GD')
BEGIN
    INSERT INTO dbo.majors (department_id, code, name, description, is_active)
    VALUES (@design_dept_id, N'GD', N'Thiết kế đồ họa (Graphic Design)', N'Chuyên ngành Thiết kế đồ họa và Mỹ thuật số', 1);
END;

UPDATE dbo.majors SET name = N'Thiết kế đồ họa (Graphic Design)', description = N'Chuyên ngành Thiết kế đồ họa và Mỹ thuật số' WHERE department_id = @design_dept_id AND code = N'GD';

DECLARE @major_gd BIGINT = (SELECT id FROM dbo.majors WHERE department_id = @design_dept_id AND code = N'GD');

-- 4. Get Role IDs
DECLARE @role_staff BIGINT   = (SELECT id FROM dbo.roles WHERE code = N'DEPARTMENT_STAFF');
DECLARE @role_lecturer BIGINT= (SELECT id FROM dbo.roles WHERE code = N'LECTURER');
DECLARE @role_student BIGINT = (SELECT id FROM dbo.roles WHERE code = N'STUDENT');
DECLARE @pwd_hash NVARCHAR(255) = N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg=='; -- Aipms@123

-- 5. Seed Users for Design Department
-- 5.1 Staff
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'staff.design@fpt.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, NULL, N'staff.design@fpt.edu.vn', @pwd_hash, N'Trần Minh Tâm', N'0905123801', NULL, N'FPTU-GD-AA01', N'Department Staff', N'ACTIVE', N'VERIFIED');
END;
DECLARE @staff_user_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'staff.design@fpt.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @staff_user_id AND role_id = @role_staff)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@staff_user_id, @role_staff);
END;

-- 5.2 Lecturers (Primary and Mentor)
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'primary.design@fe.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, NULL, N'primary.design@fe.edu.vn', @pwd_hash, N'Vũ Hoàng Anh', N'0905123802', NULL, N'FPTU-GD-001', N'Lecturer', N'ACTIVE', N'VERIFIED');
END;
DECLARE @lec1_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'primary.design@fe.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @lec1_id AND role_id = @role_lecturer)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@lec1_id, @role_lecturer);
END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_profiles WHERE user_id = @lec1_id)
BEGIN
    INSERT INTO dbo.supervisor_profiles (user_id, is_available, max_active_projects, created_at, updated_at)
    VALUES (@lec1_id, 1, 5, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'mentor.design@fe.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, NULL, N'mentor.design@fe.edu.vn', @pwd_hash, N'Đặng Thanh Thảo', N'0905123803', NULL, N'FPTU-GD-002', N'Lecturer', N'ACTIVE', N'VERIFIED');
END;
DECLARE @lec2_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'mentor.design@fe.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @lec2_id AND role_id = @role_lecturer)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@lec2_id, @role_lecturer);
END;
IF NOT EXISTS (SELECT 1 FROM dbo.supervisor_profiles WHERE user_id = @lec2_id)
BEGIN
    INSERT INTO dbo.supervisor_profiles (user_id, is_available, max_active_projects, created_at, updated_at)
    VALUES (@lec2_id, 1, 5, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

-- 5.3 Students
IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'student.design1@fpt.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, @major_gd, N'student.design1@fpt.edu.vn', @pwd_hash, N'Nguyễn Thái Dương', N'0905123811', N'DE180920', NULL, N'Student', N'ACTIVE', N'VERIFIED');
END;
DECLARE @stu1_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'student.design1@fpt.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @stu1_id AND role_id = @role_student)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@stu1_id, @role_student);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'student.design2@fpt.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, @major_gd, N'student.design2@fpt.edu.vn', @pwd_hash, N'Lê Quỳnh Trang', N'0905123812', N'DE180921', NULL, N'Student', N'ACTIVE', N'VERIFIED');
END;
DECLARE @stu2_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'student.design2@fpt.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @stu2_id AND role_id = @role_student)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@stu2_id, @role_student);
END;

IF NOT EXISTS (SELECT 1 FROM dbo.users WHERE email = N'student.design3@fpt.edu.vn')
BEGIN
    INSERT INTO dbo.users (department_id, major_id, email, password_hash, full_name, phone, student_code, employee_code, title, status, academic_profile_status)
        VALUES (@design_dept_id, @major_gd, N'student.design3@fpt.edu.vn', @pwd_hash, N'Hoàng Nhật Nam', N'0905123813', N'DE180922', NULL, N'Student', N'ACTIVE', N'VERIFIED');
END;
DECLARE @stu3_id BIGINT = (SELECT id FROM dbo.users WHERE email = N'student.design3@fpt.edu.vn');
IF NOT EXISTS (SELECT 1 FROM dbo.user_roles WHERE user_id = @stu3_id AND role_id = @role_student)
BEGIN
    INSERT INTO dbo.user_roles (user_id, role_id) VALUES (@stu3_id, @role_student);
END;

-- Repair existing demo rows created by older versions of this script.
UPDATE dbo.users SET department_id = @design_dept_id, major_id = @major_gd,
    full_name = CASE email
        WHEN N'student.design1@fpt.edu.vn' THEN N'Nguyễn Thái Dương'
        WHEN N'student.design2@fpt.edu.vn' THEN N'Lê Quỳnh Trang'
        WHEN N'student.design3@fpt.edu.vn' THEN N'Hoàng Nhật Nam'
        ELSE full_name END
WHERE email IN (N'student.design1@fpt.edu.vn', N'student.design2@fpt.edu.vn', N'student.design3@fpt.edu.vn');
COMMIT TRANSACTION;

