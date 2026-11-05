-- ==============================================================================================
-- SCRIPT: seed_demo_academic_governance.sql
-- MỤC ĐÍCH: Khởi tạo dữ liệu thực tế cho demo Hội đồng & Phê duyệt học thuật (Q-N02, BE-01 -> BE-09, E01-E14)
-- HỆ THỐNG: AI-PMS (AI Project Management System)
-- ==============================================================================================

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

USE [AI_PMS];

BEGIN TRANSACTION;
BEGIN TRY

    DECLARE @pwd_hash NVARCHAR(255) = N'AQAAAAIAAYagAAAAECDlj2anj0PYyt+p+4Y/ZoHOJK1yaPX5R0QW/kB8Q+7+HZfcCfIn2WbJH4rtYP+2sg=='; -- Aipms@123

    -- ==============================================================================================
    -- 1. CHUẨN HÓA TÊN VÀ TÀI KHOẢN NGƯỜI DÙNG (UNICODE TIẾNG VIỆT CHUẨN)
    -- ==============================================================================================
    UPDATE dbo.users SET full_name = N'Đặng Tuấn Anh', password_hash = @pwd_hash WHERE email = N'aipms.admin@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Hoàng Nam Phương', password_hash = @pwd_hash WHERE email = N'admin.secondary@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Lê Thu Hà', password_hash = @pwd_hash, department_id = 1 WHERE email = N'sep490.coordinator@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Ngô Bảo Châu', password_hash = @pwd_hash, department_id = 1 WHERE email = N'staff.it02@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Võ Thị Mai Lan', password_hash = @pwd_hash, department_id = 2 WHERE email = N'staff.ba01@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Trần Minh Tâm', password_hash = @pwd_hash WHERE email = N'staff.design@fpt.edu.vn';
    UPDATE dbo.users SET full_name = N'Phạm Đức Long', password_hash = @pwd_hash WHERE email = N'lecturer.ads01@fe.edu.vn';
    UPDATE dbo.users SET full_name = N'Trần Thị Mai', password_hash = @pwd_hash WHERE email = N'lecturer.ai01@fe.edu.vn';
    UPDATE dbo.users SET full_name = N'Nguyễn Văn Hùng', password_hash = @pwd_hash WHERE email = N'lecturer.se01@fe.edu.vn';
    UPDATE dbo.users SET full_name = N'Đỗ Quang Huy', password_hash = @pwd_hash WHERE email = N'lecturer.se02@fe.edu.vn';
    UPDATE dbo.users SET full_name = N'Vũ Hoàng Anh', password_hash = @pwd_hash WHERE email = N'primary.design@fe.edu.vn';
    UPDATE dbo.users SET full_name = N'Đặng Thanh Thảo', password_hash = @pwd_hash WHERE email = N'mentor.design@fe.edu.vn';

    -- Đảm bảo tất cả mật khẩu đồng bộ về Aipms@123
    UPDATE dbo.users SET password_hash = @pwd_hash WHERE password_hash IS NULL OR password_hash = '';

    -- Lấy ID các tài khoản quan trọng
    DECLARE @adminId BIGINT = (SELECT id FROM dbo.users WHERE email = N'aipms.admin@fpt.edu.vn');
    DECLARE @staffHaId BIGINT = (SELECT id FROM dbo.users WHERE email = N'sep490.coordinator@fpt.edu.vn');
    DECLARE @staffChauId BIGINT = (SELECT id FROM dbo.users WHERE email = N'staff.it02@fpt.edu.vn');
    DECLARE @lecturerLongId BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.ads01@fe.edu.vn');
    DECLARE @lecturerMaiId BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.ai01@fe.edu.vn');
    DECLARE @lecturerHungId BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.se01@fe.edu.vn');
    DECLARE @lecturerHuyId BIGINT = (SELECT id FROM dbo.users WHERE email = N'lecturer.se02@fe.edu.vn');

    -- ==============================================================================================
    -- 2. CẬP NHẬT RUBRIC 1 (CHÍNH THỨC, HỌC PHẦN CHUNG - COMMON, LIÊN NGÀNH)
    -- ==============================================================================================
    UPDATE dbo.rubrics 
    SET name = N'Rubric Đánh giá Đồ án Tốt nghiệp SEP490 (Học kỳ Fall 2026)',
        description = N'Rubric chuẩn hóa đánh giá đồ án tốt nghiệp đại học, áp dụng cho toàn bộ hội đồng đánh giá và sinh viên ngành CNTT & Đồ án liên ngành.',
        evaluation_scope = N'COMMON',
        project_mode = N'INTERDISCIPLINARY',
        evaluation_stage = N'DEFENSE',
        major_id = NULL,
        approval_reference = N'QĐ-ĐHFP-2026/08/QĐ-HĐKH-CNTT',
        effective_from = '2026-09-01',
        effective_to = '2026-12-31'
    WHERE id = 1;

    -- Thêm Criteria Master mới cho cây phân cấp 2 tầng (E10)
    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_ARCH_MICRO')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_ARCH_MICRO', N'1.1 Kiến trúc Vi dịch vụ, Phân rã Domain & Data Model', N'Đánh giá thiết kế phân tầng, bounded context và cơ sở dữ liệu quan hệ', 1, GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_ARCH_SEC')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_ARCH_SEC', N'1.2 Thiết kế API Contracts, Xác thực Phân quyền & Bảo mật', N'Đánh giá chuẩn REST/gRPC, JWT middleware và an toàn dữ liệu', 1, GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_CODE_CLEAN')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_CODE_CLEAN', N'2.1 Clean Architecture, Design Patterns & Tích hợp AI', N'Đánh giá cấu trúc dự án, DDD, áp dụng patterns và mô hình trí tuệ nhân tạo', 1, GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_CODE_TEST')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_CODE_TEST', N'2.2 Kiểm thử Tự động (Unit/Integration Test) & CI/CD Pipeline', N'Đánh giá test coverage, kiểm thử tích hợp DB thật và tự động hóa release', 1, GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_DEF_DEMO')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_DEF_DEMO', N'3.1 Thuyết trình Sản phẩm, Tác phong & Demo Trực quan', N'Đánh giá phong thái tự tin, demo tính năng trực tiếp ổn định', 1, GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.evaluation_criteria WHERE code = N'CRIT_DEF_QNA_DEEP')
        INSERT INTO dbo.evaluation_criteria (code, name, description, is_active, created_at, updated_at)
        VALUES (N'CRIT_DEF_QNA_DEEP', N'3.2 Nắm vững Nghiệp vụ & Trả lời Câu hỏi Phản biện', N'Đánh giá khả năng làm chủ công nghệ, giải trình giải pháp kỹ thuật trước hội đồng', 1, GETUTCDATE(), GETUTCDATE());

    -- Lấy ID các criteria con
    DECLARE @cArchMicro BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_ARCH_MICRO');
    DECLARE @cArchSec BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_ARCH_SEC');
    DECLARE @cCodeClean BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_CODE_CLEAN');
    DECLARE @cCodeTest BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_CODE_TEST');
    DECLARE @cDefDemo BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_DEF_DEMO');
    DECLARE @cDefQnaDeep BIGINT = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'CRIT_DEF_QNA_DEEP');

    -- Thêm các tiêu chí con vào Rubric 1 với parent_id trỏ về các tiêu chí gốc (id 2 = ARCH_DES, id 3 = IMPL_FUN, id 5 = DEF_QNA)
    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cArchMicro)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cArchMicro, 2, 10.00, 10.00, 1, 1, N'PLO-01', N'Sơ đồ kiến trúc vi dịch vụ, bounded context, file migration DB', GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cArchSec)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cArchSec, 2, 10.00, 10.00, 2, 1, N'PLO-02', N'Tài liệu Swagger OpenAPI, Middleware xác thực JWT, RBAC Policy', GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cCodeClean)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cCodeClean, 3, 20.00, 10.00, 1, 1, N'PLO-03', N'Source code sạch, áp dụng Clean Architecture, Dependency Inversion', GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cCodeTest)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cCodeTest, 4, 15.00, 10.00, 1, 1, N'PLO-04', N'Bộ kiểm thử tự động hơn 1.400 integration tests, GitHub Actions CI xanh 100%', GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cDefDemo)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cDefDemo, 5, 7.50, 10.00, 1, 1, N'PLO-05', N'Demo luồng nghiệp vụ chấm điểm, phân quyền học thuật trực tiếp mượt mà', GETUTCDATE(), GETUTCDATE());

    IF NOT EXISTS (SELECT 1 FROM dbo.rubric_criteria WHERE rubric_id = 1 AND criterion_id = @cDefQnaDeep)
        INSERT INTO dbo.rubric_criteria (rubric_id, criterion_id, parent_id, weight_percent, max_score, sort_order, is_required, learning_outcome, evidence_guidance, created_at, updated_at)
        VALUES (1, @cDefQnaDeep, 5, 7.50, 10.00, 2, 1, N'PLO-06', N'Sinh viên nắm vững nghiệp vụ và giải trình thấu đáo các câu hỏi của Hội đồng', GETUTCDATE(), GETUTCDATE());

    -- Thêm các mức độ chất lượng (Quality Levels) cho tất cả criteria của Rubric 1 (BE-06)
    DELETE FROM dbo.rubric_criterion_levels WHERE rubric_criterion_id IN (SELECT id FROM dbo.rubric_criteria WHERE rubric_id = 1);

    DECLARE @curRcId BIGINT;
    DECLARE curLeaf CURSOR FOR SELECT id FROM dbo.rubric_criteria WHERE rubric_id = 1;
    OPEN curLeaf;
    FETCH NEXT FROM curLeaf INTO @curRcId;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        INSERT INTO dbo.rubric_criterion_levels (rubric_criterion_id, level_order, label, descriptor, score_from, score_to, created_at, updated_at)
        VALUES
        (@curRcId, 1, N'Xuất sắc (Excellent)', N'Giải pháp kỹ thuật chuẩn mực, tối ưu hóa cao, đáp ứng vượt trội và có tính sáng tạo nổi bật.', 8.5, 10.0, GETUTCDATE(), GETUTCDATE()),
        (@curRcId, 2, N'Giỏi (Good)', N'Đáp ứng đầy đủ, chuẩn xác các tiêu chuẩn kỹ thuật, hệ thống vận hành mượt mà, tài liệu chỉn chu.', 7.0, 8.4, GETUTCDATE(), GETUTCDATE()),
        (@curRcId, 3, N'Khá (Average)', N'Đáp ứng tốt các yêu cầu chính, có một số thiếu sót nhỏ không ảnh hưởng đến khả năng triển khai.', 5.5, 6.9, GETUTCDATE(), GETUTCDATE()),
        (@curRcId, 4, N'Trung bình (Poor)', N'Hoàn thành ở mức tối thiểu, còn lỗi kỹ thuật hoặc thiếu sót cần khắc phục.', 4.0, 5.4, GETUTCDATE(), GETUTCDATE()),
        (@curRcId, 5, N'Yếu / Không đạt (Fail)', N'Chưa đạt chuẩn đầu ra tối thiểu, sản phẩm chưa hoàn thiện hoặc giải pháp không khả thi.', 0.0, 3.9, GETUTCDATE(), GETUTCDATE());
        FETCH NEXT FROM curLeaf INTO @curRcId;
    END;
    CLOSE curLeaf;
    DEALLOCATE curLeaf;

    -- Thêm lịch sử phê duyệt Four-Eyes hợp lệ cho Rubric 1
    DELETE FROM dbo.rubric_approvals WHERE rubric_id = 1;
    INSERT INTO dbo.rubric_approvals (rubric_id, submitted_by, submitted_at, decided_by, decided_at, decision, reason)
    VALUES (1, @staffHaId, '2026-08-25 08:30:00', @staffChauId, '2026-08-26 14:00:00', N'APPROVED', N'Phê duyệt áp dụng chính thức cho các Hội đồng bảo vệ đồ án tốt nghiệp Fall 2026.');

    -- ==============================================================================================
    -- 3. TẠO RUBRIC 2: CHUYÊN NGÀNH SE (MAJOR-SPECIFIC, PUBLISHED)
    -- ==============================================================================================
    DECLARE @rubric2Id BIGINT;
    IF NOT EXISTS (SELECT 1 FROM dbo.rubrics WHERE code = N'RUBRIC-SE-FA26')
    BEGIN
        INSERT INTO dbo.rubrics (department_id, academic_semester_id, code, name, description, is_active, created_by, created_at, updated_at, major_id, project_mode, evaluation_stage, evaluation_scope, effective_from, effective_to, approval_reference)
        VALUES (1, 1, N'RUBRIC-SE-FA26', N'Rubric Đánh giá Chuyên sâu Kỹ thuật Phần mềm (SE) - Fall 2026', 
                N'Tiêu chí đánh giá nâng cao về kiến trúc phần mềm, design patterns, tối ưu hiệu năng và quy chuẩn mã nguồn dành cho sinh viên chuyên ngành SE.', 
                1, @staffHaId, GETUTCDATE(), GETUTCDATE(), 1, N'SINGLE_MAJOR', N'DEFENSE', N'MAJOR_SPECIFIC', '2026-09-01', '2026-12-31', N'QĐ-ĐHFP-2026/09/QĐ-HĐKH-SE');
        SET @rubric2Id = SCOPE_IDENTITY();
        INSERT INTO dbo.rubric_versions (rubric_id, root_rubric_id, version_number, status, concurrency_token)
        VALUES (@rubric2Id, @rubric2Id, 1, N'PUBLISHED', NEWID());
    END
    ELSE
    BEGIN
        SELECT @rubric2Id = id FROM dbo.rubrics WHERE code = N'RUBRIC-SE-FA26';
    END;

    -- Thêm phê duyệt Four-Eyes cho Rubric 2
    DELETE FROM dbo.rubric_approvals WHERE rubric_id = @rubric2Id;
    INSERT INTO dbo.rubric_approvals (rubric_id, submitted_by, submitted_at, decided_by, decided_at, decision, reason)
    VALUES (@rubric2Id, @staffHaId, '2026-08-28 09:00:00', @staffChauId, '2026-08-29 11:30:00', N'APPROVED', N'Phê duyệt khung đánh giá nâng cao chuyên ngành Kỹ thuật Phần mềm.');

    -- ==============================================================================================
    -- 4. TẠO RUBRIC 3: MINH HỌA Q-N02 FOUR-EYES APPROVAL (TRẠNG THÁI PENDING_APPROVAL)
    -- ==============================================================================================
    DECLARE @rubric3Id BIGINT;
    IF NOT EXISTS (SELECT 1 FROM dbo.rubrics WHERE code = N'RUBRIC-AI-PENDING')
    BEGIN
        INSERT INTO dbo.rubrics (department_id, academic_semester_id, code, name, description, is_active, created_by, created_at, updated_at, major_id, project_mode, evaluation_stage, evaluation_scope, effective_from, effective_to, approval_reference)
        VALUES (1, 1, N'RUBRIC-AI-PENDING', N'Rubric Đánh giá Mô-đun Trí tuệ Nhân tạo & Xử lý Ngôn ngữ Tự nhiên (Đang chờ Hội đồng duyệt)', 
                N'Khung tiêu chí đánh giá mô hình học máy, pipeline xử lý dữ liệu và thuật toán AI trong đồ án liên ngành. Đang trong quy trình thẩm duyệt Four-Eyes.', 
                1, @staffHaId, GETUTCDATE(), GETUTCDATE(), 2, N'INTERDISCIPLINARY', N'DEFENSE', N'MAJOR_SPECIFIC', '2026-11-01', '2026-12-31', NULL);
        SET @rubric3Id = SCOPE_IDENTITY();
        INSERT INTO dbo.rubric_versions (rubric_id, root_rubric_id, version_number, status, concurrency_token)
        VALUES (@rubric3Id, @rubric3Id, 1, N'PENDING_APPROVAL', NEWID());
    END
    ELSE
    BEGIN
        SELECT @rubric3Id = id FROM dbo.rubrics WHERE code = N'RUBRIC-AI-PENDING';
        UPDATE dbo.rubrics SET evaluation_scope = N'MAJOR_SPECIFIC', major_id = 2 WHERE id = @rubric3Id;
        UPDATE dbo.rubric_versions SET status = N'PENDING_APPROVAL' WHERE rubric_id = @rubric3Id;
    END;

    -- Thiết lập bản ghi chờ duyệt: Lê Thu Hà gửi duyệt, chưa có người quyết định
    DELETE FROM dbo.rubric_approvals WHERE rubric_id = @rubric3Id;
    INSERT INTO dbo.rubric_approvals (rubric_id, submitted_by, submitted_at, decided_by, decided_at, decision, reason)
    VALUES (@rubric3Id, @staffHaId, '2026-11-04 10:30:00', NULL, NULL, NULL, NULL);

    -- ==============================================================================================
    -- 5. THIẾT LẬP PHƯƠNG ÁN ĐÁNH GIÁ (EVALUATION SCHEME ĐỒNG BỘ) CHO PROJECT 1
    -- ==============================================================================================
    -- Làm sạch dữ liệu đánh giá cũ của Project 1
    DELETE FROM dbo.project_result_evaluations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.student_result_evaluations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.evaluation_details WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.evaluation_finalizations WHERE evaluation_id IN (SELECT id FROM dbo.evaluations WHERE project_id = 1);
    DELETE FROM dbo.evaluation_draft_states WHERE assignment_id IN (SELECT id FROM dbo.evaluation_assignments WHERE project_id = 1);
    DELETE FROM dbo.evaluations WHERE project_id = 1;
    DELETE FROM dbo.student_results WHERE project_id = 1;
    DELETE FROM dbo.project_results WHERE project_id = 1;
    DELETE FROM dbo.final_submissions WHERE project_id = 1;
    DELETE FROM dbo.evaluation_assignments WHERE project_id = 1;
    DELETE FROM dbo.evaluation_scheme_components WHERE scheme_id IN (SELECT id FROM dbo.evaluation_schemes WHERE project_id = 1);
    DELETE FROM dbo.defense_committee_members WHERE committee_id IN (SELECT id FROM dbo.defense_committees WHERE project_id = 1);
    DELETE FROM dbo.defense_committees WHERE project_id = 1;
    DELETE FROM dbo.evaluation_schemes WHERE project_id = 1;

    DECLARE @govJson NVARCHAR(MAX) = N'{"IsDemo":false,"ApprovalStatus":"APPROVED","ApprovalReference":"QĐ-ĐHFP-2026/08/QĐ-HĐKH-CNTT","Formula":"COMPONENT_WEIGHTS","CalculationVersion":"DECIMAL_V2","RoundingMode":"AWAY_FROM_ZERO","DecimalPlaces":2}';

    DECLARE @studentsJson NVARCHAR(MAX) = N'[{"StudentId":6,"MajorId":1,"DepartmentId":1},{"StudentId":5,"MajorId":1,"DepartmentId":1},{"StudentId":3,"MajorId":2,"DepartmentId":1},{"StudentId":14,"MajorId":5,"DepartmentId":2},{"StudentId":4,"MajorId":3,"DepartmentId":1}]';

    DECLARE @snapshotJson NVARCHAR(MAX) = N'{"Scope":{"ProjectMode":"INTERDISCIPLINARY","PrimaryMajorId":1,"LeadDepartmentId":1,"Requirements":[{"MajorId":1,"MinStudents":1,"MaxStudents":3,"Description":"SE"},{"MajorId":2,"MinStudents":1,"MaxStudents":2,"Description":"AI"},{"MajorId":5,"MinStudents":1,"MaxStudents":1,"Description":"BA"}],"ConcurrencyToken":"3fa85f64-5717-4562-b3fc-2c963f66afa6"},"Team":{"Size":5,"MaxTeamSize":5,"MinDistinctMajors":3,"AllowedProposalSources":"ALL"},"OrganizationId":1,"RegistrationWindowOpen":"2026-08-15T00:00:00Z","RegistrationWindowClose":"2026-09-05T00:00:00Z","Members":[{"UserId":6,"FullName":"Nguyễn Minh Khang","MajorId":1,"IsLeader":true},{"UserId":5,"FullName":"Trần Gia Hân","MajorId":1,"IsLeader":false},{"UserId":3,"FullName":"Phạm Quốc Bảo","MajorId":2,"IsLeader":false},{"UserId":14,"FullName":"Lê Khánh Linh","MajorId":5,"IsLeader":false},{"UserId":4,"FullName":"Võ Anh Duy","MajorId":3,"IsLeader":false}],"DepartmentIds":[1,2],"MajorDepartmentIds":{"1":1,"2":1,"3":1,"5":2}}';

    INSERT INTO dbo.evaluation_schemes (root_id, version, project_id, project_period_id, name, status, pass_threshold, concurrency_token, policy_version_id, calculation_rule, created_by, created_at, published_by, published_at, governance_json, locked_at, students_json, registration_snapshot_json)
    VALUES (NULL, 1, 1, 4, N'Phương án Đánh giá Đồ án Tốt nghiệp SEP490 - AI-PMS (Hội đồng 01 - Fall 2026)', N'PUBLISHED', 5.00, NEWID(), NULL, N'DECIMAL_V2', @staffHaId, '2026-09-05 08:00:00', @staffChauId, '2026-09-06 09:30:00', @govJson, '2026-09-06 09:30:00', @studentsJson, @snapshotJson);
    DECLARE @scheme1Id BIGINT = SCOPE_IDENTITY();
    UPDATE dbo.evaluation_schemes SET root_id = @scheme1Id WHERE id = @scheme1Id;

    -- Thêm 8 thành phần đánh giá chuẩn hóa theo 3 giai đoạn (Project 100%, Student 100% cho mọi major)
    -- Giai đoạn 1: ON_GOING (3 thành phần: 30%)
    INSERT INTO dbo.evaluation_scheme_components (scheme_id, name, scope, major_id, rubric_id, project_weight_percent, student_weight_percent, required_evaluators, evaluator_weights_json, stage)
    VALUES
    (@scheme1Id, N'1. Đánh giá Tiến độ Sprint 1 (Proposal & Requirements)', N'COMMON', NULL, 1, 10.00, 10.00, 1, NULL, N'ON_GOING'),
    (@scheme1Id, N'2. Đánh giá Tiến độ Sprint 2 (Architecture & UI/UX Prototype)', N'COMMON', NULL, 1, 10.00, 10.00, 1, NULL, N'ON_GOING'),
    (@scheme1Id, N'3. Đánh giá Tiến độ Sprint 3 (Core Implementation & AI Integration)', N'COMMON', NULL, 1, 10.00, 10.00, 1, NULL, N'ON_GOING');

    -- Giai đoạn 2: COLD (2 thành phần: 20%)
    INSERT INTO dbo.evaluation_scheme_components (scheme_id, name, scope, major_id, rubric_id, project_weight_percent, student_weight_percent, required_evaluators, evaluator_weights_json, stage)
    VALUES
    (@scheme1Id, N'4. Thẩm định Báo cáo Chuyên môn & Tài liệu Kỹ thuật (Cold Report)', N'COMMON', NULL, 1, 10.00, 10.00, 1, NULL, N'COLD'),
    (@scheme1Id, N'5. Thẩm định Mã nguồn & Kiểm thử Tự động (Codebase & CI Audit)', N'COMMON', NULL, 1, 10.00, 10.00, 1, NULL, N'COLD');

    -- Giai đoạn 3: DEFENSE (3 thành phần: 50%)
    INSERT INTO dbo.evaluation_scheme_components (scheme_id, name, scope, major_id, rubric_id, project_weight_percent, student_weight_percent, required_evaluators, evaluator_weights_json, stage)
    VALUES
    (@scheme1Id, N'6. Hội đồng Bảo vệ - Điểm Đánh giá của Chủ tịch Hội đồng', N'COMMON', NULL, 1, 20.00, 20.00, 1, NULL, N'DEFENSE'),
    (@scheme1Id, N'7. Hội đồng Bảo vệ - Điểm Đánh giá của Giảng viên Phản biện 1', N'COMMON', NULL, 1, 15.00, 15.00, 1, NULL, N'DEFENSE'),
    (@scheme1Id, N'8. Hội đồng Bảo vệ - Điểm Đánh giá của Giảng viên Phản biện 2', N'COMMON', NULL, 1, 15.00, 15.00, 1, NULL, N'DEFENSE');

    -- ==============================================================================================
    -- 6. THÀNH LẬP HỘI ĐỒNG BẢO VỆ (DEFENSE COMMITTEE) CHO PROJECT 1
    -- ==============================================================================================
    INSERT INTO dbo.defense_committees (project_id, name, created_by, created_at)
    VALUES (1, N'Hội đồng Bảo vệ Đồ án Tốt nghiệp SEP490 - Hội đồng 01 (CNTT & Liên ngành)', @staffHaId, '2026-10-28 08:30:00');
    DECLARE @commId BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.defense_committee_members (committee_id, user_id, major_id)
    VALUES 
    (@commId, @lecturerLongId, 3), -- Phạm Đức Long (Chủ tịch HĐ, ADS)
    (@commId, @lecturerHungId, 1), -- Nguyễn Văn Hùng (Phản biện 1, SE)
    (@commId, @lecturerHuyId, 1),  -- Đỗ Quang Huy (Phản biện 2, SE)
    (@commId, @lecturerMaiId, 2);  -- Trần Thị Mai (Thư ký HĐ / GVHD, AI)

    -- ==============================================================================================
    -- 7. NỘP SẢN PHẨM HOÀN THIỆN (FINAL SUBMISSION) CHO PROJECT 1
    -- ==============================================================================================
    DECLARE @leaderId BIGINT = (SELECT id FROM dbo.users WHERE email = N'khangnmde180901@fpt.edu.vn');
    INSERT INTO dbo.final_submissions (project_id, project_period_id, submitted_by, submitted_at, deadline, notes, draft_concurrency_token, requirements_concurrency_token)
    VALUES (1, 4, @leaderId, '2026-11-03 16:45:00', '2026-11-04 23:59:59', 
            N'Nhóm SEP490-FA26-AIPMS xin nộp toàn bộ sản phẩm hoàn thiện của Đồ án Tốt nghiệp: Hệ thống Quản lý Đồ án Tốt nghiệp Thông minh Hỗ trợ AI (AI-PMS). Bao gồm đầy đủ Source code, Báo cáo Kiến trúc, Video Demo và Bộ kiểm thử tự động.', 
            NEWID(), NEWID());
    DECLARE @finalSubId BIGINT = SCOPE_IDENTITY();

    -- ==============================================================================================
    -- 8. PHÂN CÔNG GIẢNG VIÊN VÀ CHẤM ĐIỂM THỰC TẾ (EVALUATION ASSIGNMENTS & RESULTS)
    -- ==============================================================================================
    -- Lấy ID của 3 thành phần bảo vệ chính
    DECLARE @compChairId BIGINT = (SELECT id FROM dbo.evaluation_scheme_components WHERE scheme_id = @scheme1Id AND name LIKE N'%Chủ tịch%');
    DECLARE @compRev1Id BIGINT = (SELECT id FROM dbo.evaluation_scheme_components WHERE scheme_id = @scheme1Id AND name LIKE N'%Phản biện 1%');
    DECLARE @compRev2Id BIGINT = (SELECT id FROM dbo.evaluation_scheme_components WHERE scheme_id = @scheme1Id AND name LIKE N'%Phản biện 2%');

    -- Phân công Chủ tịch Hội đồng (@lecturerLongId - Phạm Đức Long)
    INSERT INTO dbo.evaluation_assignments (project_id, evaluator_id, rubric_id, project_period_id, department_id, evaluation_type, status, assigned_by, assigned_at, concurrency_token, scope, component_id, policy_version_id, scope_snapshot_json, stage)
    VALUES (1, @lecturerLongId, 1, 4, 1, N'COMMITTEE', N'ACTIVE', @staffHaId, '2026-11-01 08:00:00', NEWID(), N'COMMON', @compChairId, 1, N'{"FinalSubmissionId":' + CAST(@finalSubId AS NVARCHAR(20)) + N'}', N'DEFENSE');
    DECLARE @asgnChair BIGINT = SCOPE_IDENTITY();

    -- Phân công Phản biện 1 (@lecturerHungId - Nguyễn Văn Hùng)
    INSERT INTO dbo.evaluation_assignments (project_id, evaluator_id, rubric_id, project_period_id, department_id, evaluation_type, status, assigned_by, assigned_at, concurrency_token, scope, component_id, policy_version_id, scope_snapshot_json, stage)
    VALUES (1, @lecturerHungId, 1, 4, 1, N'COMMITTEE', N'ACTIVE', @staffHaId, '2026-11-01 08:00:00', NEWID(), N'COMMON', @compRev1Id, 1, N'{"FinalSubmissionId":' + CAST(@finalSubId AS NVARCHAR(20)) + N'}', N'DEFENSE');
    DECLARE @asgnRev1 BIGINT = SCOPE_IDENTITY();

    -- Phân công Phản biện 2 (@lecturerHuyId - Đỗ Quang Huy)
    INSERT INTO dbo.evaluation_assignments (project_id, evaluator_id, rubric_id, project_period_id, department_id, evaluation_type, status, assigned_by, assigned_at, concurrency_token, scope, component_id, policy_version_id, scope_snapshot_json, stage)
    VALUES (1, @lecturerHuyId, 1, 4, 1, N'COMMITTEE', N'ACTIVE', @staffHaId, '2026-11-01 08:00:00', NEWID(), N'COMMON', @compRev2Id, 1, N'{"FinalSubmissionId":' + CAST(@finalSubId AS NVARCHAR(20)) + N'}', N'DEFENSE');
    DECLARE @asgnRev2 BIGINT = SCOPE_IDENTITY();

    -- Tạo phiếu chấm và điểm chi tiết của Chủ tịch Hội đồng (8.8 / 10)
    INSERT INTO dbo.evaluations (project_id, evaluator_id, rubric_id, evaluation_type, status, total_score, comments, evaluated_at, created_at, updated_at)
    VALUES (1, @lecturerLongId, 1, N'COMMITTEE', N'FINALIZED', 8.80, 
            N'Đồ án có tính thực tiễn cao, giải quyết xuất sắc bài toán điều phối đồ án tốt nghiệp. Nhóm thuyết trình tự tin, demo trực tiếp mượt mà, trả lời phản biện thuyết phục.', 
            '2026-11-05 09:30:00', '2026-11-05 09:00:00', '2026-11-05 09:30:00');
    DECLARE @evalChair BIGINT = SCOPE_IDENTITY();

    -- Ghi nhận điểm chi tiết cho các tiêu chí của Rubric 1
    INSERT INTO dbo.evaluation_details (evaluation_id, rubric_criterion_id, score, comments, created_at, updated_at)
    VALUES
    (@evalChair, 1, 8.50, N'Tài liệu đặc tả yêu cầu SRS và thiết kế chi tiết, rõ ràng.', GETUTCDATE(), GETUTCDATE()),
    (@evalChair, 2, 8.80, N'Mô hình kiến trúc phân rã rõ ràng, CSDL thiết kế chuẩn hóa tốt.', GETUTCDATE(), GETUTCDATE()),
    (@evalChair, 3, 9.00, N'Hệ thống hoàn thiện đầy đủ các phân hệ quản lý đồ án, rubric và chấm điểm.', GETUTCDATE(), GETUTCDATE()),
    (@evalChair, 4, 8.70, N'Hệ thống kiểm thử tự động bao phủ rộng, CI/CD chạy ổn định.', GETUTCDATE(), GETUTCDATE()),
    (@evalChair, 5, 9.00, N'Nhóm nắm rất chắc quy trình và giải thích thấu đáo các thắc mắc của hội đồng.', GETUTCDATE(), GETUTCDATE());

    INSERT INTO dbo.evaluation_finalizations (evaluation_id, final_submission_id, finalized_by, finalized_at, snapshot_json)
    VALUES (@evalChair, @finalSubId, @lecturerLongId, '2026-11-05 09:35:00', N'{"TotalScore":8.80,"Evaluator":"Phạm Đức Long","Role":"Chủ tịch Hội đồng"}');

    -- Tạo phiếu chấm và điểm chi tiết của Phản biện 1 (9.0 / 10)
    INSERT INTO dbo.evaluations (project_id, evaluator_id, rubric_id, evaluation_type, status, total_score, comments, evaluated_at, created_at, updated_at)
    VALUES (1, @lecturerHungId, 1, N'COMMITTEE', N'FINALIZED', 9.00, 
            N'Mã nguồn được tổ chức rất chuyên nghiệp. Áp dụng xuất sắc các nguyên lý thiết kế SOLID. Hệ thống kiểm thử tự động hơn 1.400 integration test là điểm cộng vượt trội.', 
            '2026-11-05 09:40:00', '2026-11-05 09:10:00', '2026-11-05 09:40:00');
    DECLARE @evalRev1 BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.evaluation_details (evaluation_id, rubric_criterion_id, score, comments, created_at, updated_at)
    VALUES
    (@evalRev1, 1, 9.00, N'Hợp đồng API OpenAPI và đặc tả nghiệp vụ đồng bộ chuẩn xác.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev1, 2, 9.00, N'Thiết kế microservices và event-driven rất hợp lý.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev1, 3, 9.50, N'Codebase cực kỳ chỉn chu, áp dụng Clean Code mẫu mực.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev1, 4, 9.00, N'Bộ test integration phủ rộng, bắt đúng các edge case nghiệp vụ phức tạp.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev1, 5, 8.50, N'Sinh viên làm chủ công nghệ và tự tin giải đáp các câu hỏi hóc búa.', GETUTCDATE(), GETUTCDATE());

    INSERT INTO dbo.evaluation_finalizations (evaluation_id, final_submission_id, finalized_by, finalized_at, snapshot_json)
    VALUES (@evalRev1, @finalSubId, @lecturerHungId, '2026-11-05 09:45:00', N'{"TotalScore":9.00,"Evaluator":"Nguyễn Văn Hùng","Role":"Phản biện 1"}');

    -- Tạo phiếu chấm và điểm chi tiết của Phản biện 2 (8.6 / 10)
    INSERT INTO dbo.evaluations (project_id, evaluator_id, rubric_id, evaluation_type, status, total_score, comments, evaluated_at, created_at, updated_at)
    VALUES (1, @lecturerHuyId, 1, N'COMMITTEE', N'FINALIZED', 8.60, 
            N'Đề tài có độ khó cao, tích hợp công nghệ AI đúng hướng. Giao diện người dùng thân thiện, trải nghiệm trơn tru. Đánh giá cao nỗ lực hoàn thiện đồ án của toàn nhóm.', 
            '2026-11-05 09:45:00', '2026-11-05 09:15:00', '2026-11-05 09:45:00');
    DECLARE @evalRev2 BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.evaluation_details (evaluation_id, rubric_criterion_id, score, comments, created_at, updated_at)
    VALUES
    (@evalRev2, 1, 8.50, N'Đầy đủ hồ sơ phân tích yêu cầu phần mềm.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev2, 2, 8.50, N'Cấu trúc dữ liệu và xử lý đồng thời (concurrency token) chuẩn.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev2, 3, 8.80, N'Xử lý logic nhất quán, ít phụ thuộc chéo.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev2, 4, 8.50, N'Quy trình CI/CD hoàn thiện, tự động chạy test khi mở PR.', GETUTCDATE(), GETUTCDATE()),
    (@evalRev2, 5, 8.70, N'Minh họa tính năng chấm điểm và phê duyệt Four-Eyes sinh động.', GETUTCDATE(), GETUTCDATE());

    INSERT INTO dbo.evaluation_finalizations (evaluation_id, final_submission_id, finalized_by, finalized_at, snapshot_json)
    VALUES (@evalRev2, @finalSubId, @lecturerHuyId, '2026-11-05 09:50:00', N'{"TotalScore":8.60,"Evaluator":"Đỗ Quang Huy","Role":"Phản biện 2"}');

    -- ==============================================================================================
    -- 9. CÔNG BỐ KẾT QUẢ ĐỒ ÁN VÀ ĐIỂM SINH VIÊN (OFFICIAL RESULTS PUBLISHED)
    -- ==============================================================================================
    -- Điểm trung bình Hội đồng = (8.80 + 9.00 + 8.60) / 3 = 8.80
    INSERT INTO dbo.project_results (project_id, final_submission_id, published_by, published_at, snapshot_json)
    VALUES (1, @finalSubId, @staffChauId, '2026-11-05 10:00:00', 
            N'{"ProjectId":1,"ProjectCode":"SEP490-FA26-AIPMS","ProjectTitle":"Hệ thống quản lý đồ án tốt nghiệp thông minh hỗ trợ AI (AI-PMS)","OverallScore":8.80,"Classification":"XUẤT SẮC (EXCELLENT)","PublishedBy":"Ngô Bảo Châu","CouncilDecision":"PASSED"}');
    DECLARE @projResultId BIGINT = SCOPE_IDENTITY();

    INSERT INTO dbo.project_result_evaluations (result_id, evaluation_id)
    VALUES 
    (@projResultId, @evalChair),
    (@projResultId, @evalRev1),
    (@projResultId, @evalRev2);

    -- Công bố kết quả cho từng sinh viên trong nhóm Project 1
    DECLARE @students TABLE (id BIGINT, major_id BIGINT, score DECIMAL(5,2));
    INSERT INTO @students VALUES 
    (6, 1, 8.95),  -- Khang (Leader)
    (5, 1, 8.85),  -- Hân (SE)
    (3, 2, 8.75),  -- Bảo (AI)
    (14, 5, 8.70), -- Linh (BA)
    (4, 3, 8.65);  -- Duy (ADS)

    INSERT INTO dbo.student_results (project_id, student_id, major_id, scheme_id, total_score, pass_threshold, outcome, calculation_rule, published_by, published_at, snapshot_json)
    SELECT 1, s.id, s.major_id, @scheme1Id, s.score, 5.00, N'PASSED', N'DECIMAL_V2', @staffChauId, '2026-11-05 10:00:00',
           CONCAT(N'{"StudentId":', s.id, N',"TotalScore":', s.score, N',"PassThreshold":5.00,"Outcome":"PASSED","Classification":"XUẤT SẮC"}')
    FROM @students s;

    -- Ghi nhận mapping đánh giá của sinh viên
    INSERT INTO dbo.student_result_evaluations (result_id, evaluation_id)
    SELECT sr.id, e.id
    FROM dbo.student_results sr
    CROSS JOIN (VALUES (@evalChair), (@evalRev1), (@evalRev2)) e(id)
    WHERE sr.project_id = 1;

    -- Cập nhật trạng thái đồ án Project 1 thành COMPLETED (Bảo vệ thành công)
    UPDATE dbo.projects 
    SET status = N'COMPLETED',
        completed_at = '2026-11-05 10:00:00'
    WHERE id = 1;

    COMMIT TRANSACTION;
    PRINT N'SUCCESS: Đã khởi tạo hoàn tất dữ liệu mẫu học thuật thực tế cho AI_PMS!';

END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    DECLARE @errMsg NVARCHAR(4000) = ERROR_MESSAGE();
    RAISERROR(@errMsg, 16, 1);
END CATCH;
