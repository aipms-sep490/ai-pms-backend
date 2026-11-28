/*
 * AI-PMS Pilot 3-Track Configuration Package (TK-W4-14)
 * Idempotent seed for Organization, 2 Departments (IT & MKT), Majors, Specializations,
 * Active Pilot Semester (15w, 6 ProjectPeriods with POL-06 non-blocking Defense), Calendars, Policy Snapshot,
 * 3 Rubrics (100.00% weights + 4 criterion levels + PUBLISHED versions),
 * MilestoneTemplate (PUBLISHED), 3 ReviewGateTemplates (G1-G3, PUBLISHED),
 * and 3 PUBLISHED AcademicBlueprints (IT_ONLY, MKT_ONLY, IT_MKT with INT-* & dual approval).
 *
 * SECURITY: Contains NO user accounts, NO password hashes, NO backdoor credentials.
 * Requires at least one ACTIVE user in dbo.users (or SESSION_CONTEXT('pilot_actor_id')).
 * Lives exclusively in db/pilot/ (never in db/changes/).
 */
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

DECLARE @actorId bigint = COALESCE(
    TRY_CONVERT(bigint, SESSION_CONTEXT(N'pilot_actor_id')),
    (
        SELECT TOP (1) u.id
        FROM dbo.users u
        JOIN dbo.user_roles ur ON ur.user_id = u.id
        JOIN dbo.roles r ON r.id = ur.role_id
        WHERE u.status = N'ACTIVE' AND r.code IN (N'ADMIN', N'DEPARTMENT_STAFF')
        ORDER BY CASE WHEN r.code = N'ADMIN' THEN 0 ELSE 1 END, u.id
    ),
    (SELECT TOP (1) id FROM dbo.users WHERE status = N'ACTIVE' ORDER BY id)
);

IF @actorId IS NULL
    THROW 51014, N'Pilot seed requires at least one ACTIVE user in dbo.users (or pass SESSION_CONTEXT pilot_actor_id).', 1;

DECLARE @semesterCode nvarchar(50) = COALESCE(
    NULLIF(CONVERT(nvarchar(50), SESSION_CONTEXT(N'pilot_semester_code')), N''),
    N'PILOT-FALL2026'
);

-- 1. Organization, Departments, Majors & Specializations
MERGE dbo.organizations AS t
USING (VALUES (N'PILOT', N'FPT University — Pilot Academic Organization', N'Canonical 3-track pilot organization')) AS s(code, name, description)
ON t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (code, name, description, is_active) VALUES (s.code, s.name, s.description, 1);

DECLARE @orgId bigint = (SELECT id FROM dbo.organizations WHERE code = N'PILOT');

MERGE dbo.departments AS t
USING (VALUES
    (@orgId, N'IT', N'Khoa Công nghệ Thông tin'),
    (@orgId, N'MKT', N'Khoa Truyền thông & Marketing')
) AS s(organization_id, code, name)
ON t.organization_id = s.organization_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (organization_id, code, name, is_active) VALUES (s.organization_id, s.code, s.name, 1);

DECLARE @deptItId bigint = (SELECT id FROM dbo.departments WHERE organization_id = @orgId AND code = N'IT');
DECLARE @deptMktId bigint = (SELECT id FROM dbo.departments WHERE organization_id = @orgId AND code = N'MKT');

MERGE dbo.majors AS t
USING (VALUES
    (@deptItId, N'SE', N'Kỹ thuật phần mềm'),
    (@deptMktId, N'MKT', N'Digital Marketing')
) AS s(department_id, code, name)
ON t.department_id = s.department_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (department_id, code, name, is_active) VALUES (s.department_id, s.code, s.name, 1);

DECLARE @majorSeId bigint = (SELECT id FROM dbo.majors WHERE department_id = @deptItId AND code = N'SE');
DECLARE @majorMktId bigint = (SELECT id FROM dbo.majors WHERE department_id = @deptMktId AND code = N'MKT');

MERGE dbo.department_specializations AS t
USING (VALUES
    (@deptItId, @majorSeId, N'SE-WEB', N'Web & Enterprise Systems', N'Phát triển ứng dụng web và hệ thống doanh nghiệp'),
    (@deptItId, @majorSeId, N'SE-AI', N'Applied AI Engineering', N'Kỹ thuật AI ứng dụng'),
    (@deptMktId, @majorMktId, N'MKT-DIG', N'Digital Performance Marketing', N'Quảng cáo số và tăng trưởng'),
    (@deptMktId, @majorMktId, N'MKT-BRAND', N'Brand & Content Strategy', N'Chiến lược thương hiệu và nội dung')
) AS s(department_id, major_id, code, name, description)
ON t.department_id = s.department_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (department_id, major_id, code, name, description, is_active)
    VALUES (s.department_id, s.major_id, s.code, s.name, s.description, 1);

-- 2. Active Pilot Semester (15+ weeks)
MERGE dbo.academic_semesters AS t
USING (VALUES (@orgId, @semesterCode, N'Học kỳ Thí điểm 3 Track (Fall 2026)', CAST('2026-09-07' AS date), CAST('2026-12-25' AS date), N'ACTIVE'))
    AS s(organization_id, code, name, start_date, end_date, status)
ON t.organization_id = s.organization_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (organization_id, code, name, start_date, end_date, status)
    VALUES (s.organization_id, s.code, s.name, s.start_date, s.end_date, s.status);

DECLARE @semesterId bigint = (SELECT id FROM dbo.academic_semesters WHERE organization_id = @orgId AND code = @semesterCode);

-- 3. Milestone Template (PUBLISHED, 3 Gate-aligned milestones)
IF NOT EXISTS (SELECT 1 FROM dbo.milestone_templates WHERE name = N'PILOT-3TRACK-MILESTONES')
BEGIN
    INSERT dbo.milestone_templates(name, description, status, created_by)
    VALUES (N'PILOT-3TRACK-MILESTONES', N'Bộ mốc chuẩn 15 tuần cho 3 Track thí điểm', N'ACTIVE', @actorId);
END;
DECLARE @msTplId bigint = (SELECT TOP (1) id FROM dbo.milestone_templates WHERE name = N'PILOT-3TRACK-MILESTONES' ORDER BY id);

IF NOT EXISTS (SELECT 1 FROM dbo.milestone_template_versions WHERE milestone_template_id = @msTplId AND version_number = 1)
BEGIN
    INSERT dbo.milestone_template_versions(milestone_template_id, version_number, status, created_by)
    VALUES (@msTplId, 1, N'PUBLISHED', @actorId);
END;
DECLARE @msVerId bigint = (SELECT id FROM dbo.milestone_template_versions WHERE milestone_template_id = @msTplId AND version_number = 1);

IF NOT EXISTS (SELECT 1 FROM dbo.milestone_template_items WHERE milestone_template_version_id = @msVerId)
BEGIN
    INSERT dbo.milestone_template_items(milestone_template_version_id, title, description, start_offset_days, due_offset_days, sort_order)
    VALUES
        (@msVerId, N'G1 — Khởi tạo kiến trúc & Định vị sản phẩm', N'Hoàn tất hồ sơ G1', 0, 21, 1),
        (@msVerId, N'G2 — Phiên bản cốt lõi & Thực thi chiến dịch', N'Hoàn tất hồ sơ G2', 22, 49, 2),
        (@msVerId, N'G3 — Tích hợp hoàn chỉnh & Tiền bảo vệ', N'Hoàn tất hồ sơ G3', 50, 77, 3);
END;

-- 4. Six Canonical Project Periods (Chronological + POL-06 Non-Blocking Defense)
MERGE dbo.project_periods AS t
USING (VALUES
    (@semesterId, N'PILOT-REG', N'Đợt 1: Lập nhóm & Đăng ký đề tài', N'REGISTRATION', CAST('2026-09-07T00:00:00' AS datetime2(0)), CAST('2026-09-18T23:59:59' AS datetime2(0)), N'ACTIVE', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', NULL, NULL, 0),
    (@semesterId, N'PILOT-REV', N'Đợt 2: Thẩm định đề cương (N3)', N'PROJECT_REVIEW', CAST('2026-09-14T00:00:00' AS datetime2(0)), CAST('2026-09-25T23:59:59' AS datetime2(0)), N'ACTIVE', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', NULL, NULL, 0),
    (@semesterId, N'PILOT-SUP', N'Đợt 3: Ghép GVHD & Mentor', N'SUPERVISOR_SELECTION', CAST('2026-09-14T00:00:00' AS datetime2(0)), CAST('2026-09-28T23:59:59' AS datetime2(0)), N'ACTIVE', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', NULL, NULL, 0),
    (@semesterId, N'PILOT-EXEC', N'Đợt 4: Thực thi Sprint & Gate G1-G3', N'EXECUTION', CAST('2026-09-28T00:00:00' AS datetime2(0)), CAST('2026-12-04T23:59:59' AS datetime2(0)), N'ACTIVE', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', @msTplId, @msVerId, 0),
    (@semesterId, N'PILOT-FINAL', N'Đợt 5: Nộp & Khóa Final', N'FINAL_SUBMISSION', CAST('2026-12-05T00:00:00' AS datetime2(0)), CAST('2026-12-11T23:59:59' AS datetime2(0)), N'UPCOMING', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', NULL, NULL, 0),
    (@semesterId, N'PILOT-EVAL', N'Đợt 6: Chấm Cold, Defense & Công bố', N'EVALUATION', CAST('2026-12-12T00:00:00' AS datetime2(0)), CAST('2026-12-23T23:59:59' AS datetime2(0)), N'UPCOMING', 3, 5, 1, 5, 'SINGLE_MAJOR,INTERDISCIPLINARY', 'PUBLISHED_TOPIC,STUDENT_PROPOSAL', NULL, NULL, 0)
) AS s(academic_semester_id, code, name, period_type, start_at, end_at, status, min_team_size, max_team_size, min_distinct_majors, max_projects_per_supervisor, allowed_project_modes, allowed_proposal_sources, milestone_template_id, milestone_template_version_id, enable_defense_room_scheduling)
ON t.academic_semester_id = s.academic_semester_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (academic_semester_id, code, name, period_type, start_at, end_at, status, min_team_size, max_team_size, min_distinct_majors, max_projects_per_supervisor, allowed_project_modes, allowed_proposal_sources, milestone_template_id, milestone_template_version_id, enable_defense_room_scheduling)
    VALUES (s.academic_semester_id, s.code, s.name, s.period_type, s.start_at, s.end_at, s.status, s.min_team_size, s.max_team_size, s.min_distinct_majors, s.max_projects_per_supervisor, s.allowed_project_modes, s.allowed_proposal_sources, s.milestone_template_id, s.milestone_template_version_id, s.enable_defense_room_scheduling);

DECLARE @regPeriodId bigint = (SELECT id FROM dbo.project_periods WHERE academic_semester_id = @semesterId AND code = N'PILOT-REG');
DECLARE @revPeriodId bigint = (SELECT id FROM dbo.project_periods WHERE academic_semester_id = @semesterId AND code = N'PILOT-REV');
DECLARE @execPeriodId bigint = (SELECT id FROM dbo.project_periods WHERE academic_semester_id = @semesterId AND code = N'PILOT-EXEC');

-- Period Calendars (Mon-Fri working days + VN holiday sample)
MERGE dbo.project_period_calendars AS t
USING (VALUES (@revPeriodId, 62, N'["2026-09-02"]'), (@execPeriodId, 62, N'["2026-09-02"]')) AS s(project_period_id, working_days_of_week, holidays_json)
ON t.project_period_id = s.project_period_id
WHEN NOT MATCHED THEN
    INSERT (project_period_id, working_days_of_week, holidays_json)
    VALUES (s.project_period_id, s.working_days_of_week, s.holidays_json);

-- Locked Period Policy Version
IF NOT EXISTS (SELECT 1 FROM dbo.period_policy_versions WHERE project_period_id = @regPeriodId AND version = 1)
BEGIN
    INSERT dbo.period_policy_versions(project_period_id, version, status, effective_from, effective_to, snapshot_json, concurrency_token, created_by, created_at)
    VALUES (
        @regPeriodId, 1, N'LOCKED', '2026-09-07T00:00:00', '2026-12-25T23:59:59',
        N'{"AllowedProjectModes":"SINGLE_MAJOR,INTERDISCIPLINARY","AllowedProposalSources":"PUBLISHED_TOPIC,STUDENT_PROPOSAL","MinTeamSize":3,"MaxTeamSize":5,"MinDistinctMajors":1,"MaxProjectsPerSupervisor":5,"RequireWorkingAgreement":false,"SupervisorInvitationExpiryDays":7,"MaxPendingSupervisorRequestsPerSlot":2}',
        NEWID(), @actorId, SYSUTCDATETIME()
    );
END;
DECLARE @policyVerId bigint = (SELECT id FROM dbo.period_policy_versions WHERE project_period_id = @regPeriodId AND version = 1);

-- 5. Evaluation Criteria & 3 Pilot Rubrics (Weights sum strictly to 100.00%, 4 quality levels, PUBLISHED versions)
MERGE dbo.evaluation_criteria AS t
USING (VALUES
    (N'PILOT-TECH', N'Kỹ thuật, Kiến trúc & Bảo mật hệ thống'),
    (N'PILOT-MKT', N'Chiến lược Marketing, KPI & Dữ liệu chiến dịch'),
    (N'PILOT-INT', N'Tích hợp liên ngành IT × MKT (Product-Market Fit & Tracking)'),
    (N'PILOT-IND', N'Trách nhiệm cá nhân & Chất lượng đầu ra riêng')
) AS s(code, name)
ON t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (code, name, is_active) VALUES (s.code, s.name, 1);

DECLARE @critTechId bigint = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'PILOT-TECH');
DECLARE @critMktId bigint = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'PILOT-MKT');
DECLARE @critIntId bigint = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'PILOT-INT');
DECLARE @critIndId bigint = (SELECT id FROM dbo.evaluation_criteria WHERE code = N'PILOT-IND');

MERGE dbo.rubrics AS t
USING (VALUES
    (@deptItId, NULL, @semesterId, N'PILOT-RUB-IT', N'Rubric chuẩn Track IT_ONLY (POL-01)', 'SINGLE_MAJOR', 'DEFENSE', 'COMMON', NULL, @actorId),
    (@deptMktId, NULL, @semesterId, N'PILOT-RUB-MKT', N'Rubric chuẩn Track MKT_ONLY (POL-01)', 'SINGLE_MAJOR', 'DEFENSE', 'COMMON', NULL, @actorId),
    (@deptItId, @deptMktId, @semesterId, N'PILOT-RUB-ITMKT', N'Rubric chuẩn Track IT_MKT (POL-01 40/40/20)', 'INTERDISCIPLINARY', 'DEFENSE', 'COMMON', NULL, @actorId)
) AS s(department_id, participating_department_id, academic_semester_id, code, name, project_mode, evaluation_stage, evaluation_scope, major_id, created_by)
ON t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (department_id, participating_department_id, academic_semester_id, code, name, is_active, project_mode, evaluation_stage, evaluation_scope, major_id, created_by)
    VALUES (s.department_id, s.participating_department_id, s.academic_semester_id, s.code, s.name, 1, s.project_mode, s.evaluation_stage, s.evaluation_scope, s.major_id, s.created_by);

DECLARE @rubItId bigint = (SELECT id FROM dbo.rubrics WHERE code = N'PILOT-RUB-IT');
DECLARE @rubMktId bigint = (SELECT id FROM dbo.rubrics WHERE code = N'PILOT-RUB-MKT');
DECLARE @rubItMktId bigint = (SELECT id FROM dbo.rubrics WHERE code = N'PILOT-RUB-ITMKT');

MERGE dbo.rubric_criteria AS t
USING (VALUES
    (@rubItId, @critTechId, 60.00, 10.00, 1, 1),
    (@rubItId, @critIndId, 40.00, 10.00, 2, 1),
    (@rubMktId, @critMktId, 60.00, 10.00, 1, 1),
    (@rubMktId, @critIndId, 40.00, 10.00, 2, 1),
    (@rubItMktId, @critTechId, 40.00, 10.00, 1, 1),
    (@rubItMktId, @critMktId, 40.00, 10.00, 2, 1),
    (@rubItMktId, @critIntId, 20.00, 10.00, 3, 1)
) AS s(rubric_id, criterion_id, weight_percent, max_score, sort_order, is_required)
ON t.rubric_id = s.rubric_id AND t.criterion_id = s.criterion_id
WHEN NOT MATCHED THEN
    INSERT (rubric_id, criterion_id, weight_percent, max_score, sort_order, is_required)
    VALUES (s.rubric_id, s.criterion_id, s.weight_percent, s.max_score, s.sort_order, s.is_required);

-- 4 Descriptive Quality Levels for every pilot rubric criterion
MERGE dbo.rubric_criterion_levels AS t
USING (
    SELECT rc.id AS rubric_criterion_id, v.level_order, v.label, v.descriptor, v.score_from, v.score_to
    FROM dbo.rubric_criteria rc
    CROSS JOIN (VALUES
        (1, N'Chưa đạt (Liệt < 4.0)', N'Thiếu minh chứng cốt lõi hoặc không đáp ứng yêu cầu tối thiểu.', 0.00, 3.99),
        (2, N'Trung bình', N'Đáp ứng yêu cầu cơ bản, còn hạn chế về độ hoàn thiện.', 4.00, 5.99),
        (3, N'Khá', N'Đầy đủ chức năng/phân tích, kiểm chứng rõ ràng.', 6.00, 7.99),
        (4, N'Tốt / Xuất sắc', N'Chất lượng triển khai thực tế cao, minh chứng đầy đủ.', 8.00, 10.00)
    ) AS v(level_order, label, descriptor, score_from, score_to)
    WHERE rc.rubric_id IN (@rubItId, @rubMktId, @rubItMktId)
) AS s
ON t.rubric_criterion_id = s.rubric_criterion_id AND t.level_order = s.level_order
WHEN NOT MATCHED THEN
    INSERT (rubric_criterion_id, level_order, label, descriptor, score_from, score_to)
    VALUES (s.rubric_criterion_id, s.level_order, s.label, s.descriptor, s.score_from, s.score_to);

-- Published Rubric Versions
MERGE dbo.rubric_versions AS t
USING (VALUES (@rubItId, @rubItId, 1, 'PUBLISHED'), (@rubMktId, @rubMktId, 1, 'PUBLISHED'), (@rubItMktId, @rubItMktId, 1, 'PUBLISHED'))
    AS s(rubric_id, root_rubric_id, version_number, status)
ON t.rubric_id = s.rubric_id
WHEN NOT MATCHED THEN
    INSERT (rubric_id, root_rubric_id, version_number, status, concurrency_token)
    VALUES (s.rubric_id, s.root_rubric_id, s.version_number, s.status, NEWID());

-- 6. Three ReviewGateTemplates (PUBLISHED) with G1-G3 & Requirements
MERGE dbo.review_gate_templates AS t
USING (VALUES
    (NULL, 'PILOT-GATE-IT-ONLY', N'Review Gate chuẩn IT_ONLY (G1-G3)', 'SINGLE_MAJOR', N'IT_ONLY', 'PUBLISHED', 1, @actorId),
    (NULL, 'PILOT-GATE-MKT-ONLY', N'Review Gate chuẩn MKT_ONLY (G1-G3)', 'SINGLE_MAJOR', N'MKT_ONLY', 'PUBLISHED', 1, @actorId),
    (NULL, 'PILOT-GATE-IT-MKT', N'Review Gate chuẩn IT_MKT (G1-G3 liên ngành)', 'INTERDISCIPLINARY', N'IT_MKT', 'PUBLISHED', 1, @actorId)
) AS s(project_period_id, code, name, mode, major, status, version, created_by)
ON t.code = s.code AND t.version = s.version
WHEN NOT MATCHED THEN
    INSERT (project_period_id, code, name, mode, major, status, version, concurrency_token, created_by)
    VALUES (s.project_period_id, s.code, s.name, s.mode, s.major, s.status, s.version, NEWID(), s.created_by);

DECLARE @gateItId bigint = (SELECT id FROM dbo.review_gate_templates WHERE code = 'PILOT-GATE-IT-ONLY' AND version = 1);
DECLARE @gateMktId bigint = (SELECT id FROM dbo.review_gate_templates WHERE code = 'PILOT-GATE-MKT-ONLY' AND version = 1);
DECLARE @gateItMktId bigint = (SELECT id FROM dbo.review_gate_templates WHERE code = 'PILOT-GATE-IT-MKT' AND version = 1);

MERGE dbo.review_gate_definitions AS t
USING (
    SELECT tpl.id AS template_id, g.code, g.sequence, g.title, g.objective, g.blocks_defense, g.weight,
           CASE WHEN tpl.mode = 'INTERDISCIPLINARY' THEN 'DUAL_DEPARTMENT' ELSE 'INDEPENDENT_REVIEWER' END AS reviewer_policy,
           CASE WHEN tpl.mode = 'INTERDISCIPLINARY' THEN 'DUAL_DEPARTMENT' ELSE 'COMMON' END AS review_scope
    FROM (VALUES (@gateItId, 'SINGLE_MAJOR'), (@gateMktId, 'SINGLE_MAJOR'), (@gateItMktId, 'INTERDISCIPLINARY')) AS tpl(id, mode)
    CROSS JOIN (VALUES
        ('G1', 1, N'Gate 1 — Phân tích & Kiến trúc / Định vị', N'Kiểm tra phạm vi, kiến trúc hoặc chiến lược ban đầu', 0, CAST(30.00 AS decimal(5,2))),
        ('G2', 2, N'Gate 2 — Phiên bản cốt lõi & Đóng góp cá nhân', N'Kiểm tra sản phẩm giữa kỳ và bằng chứng từng SV', 0, CAST(35.00 AS decimal(5,2))),
        ('G3', 3, N'Gate 3 — Hoàn thiện & Điều kiện tiền bảo vệ', N'Chốt điều kiện ra bảo vệ trước hội đồng', 1, CAST(35.00 AS decimal(5,2)))
    ) AS g(code, sequence, title, objective, blocks_defense, weight)
) AS s
ON t.template_id = s.template_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (template_id, code, sequence, title, objective, required, block_next_gate, blocks_defense, weight, reviewer_policy, review_scope)
    VALUES (s.template_id, s.code, s.sequence, s.title, s.objective, 1, 1, s.blocks_defense, s.weight, s.reviewer_policy, s.review_scope);

-- Common + INDIVIDUAL(EACH_STUDENT) + INT-* requirements
MERGE dbo.review_gate_requirements AS t
USING (
    SELECT d.id AS gate_definition_id, CONCAT(d.code, '-CORE') AS code,
           CONCAT(d.code, N' — Tài liệu nghiệm thu chung') AS title,
           'DOCUMENT' AS type, 'COMMON' AS scope, 'HUMAN' AS verification, 'PROJECT' AS target_cardinality, NULL AS policy_tag
    FROM dbo.review_gate_definitions d WHERE d.template_id IN (@gateItId, @gateMktId, @gateItMktId)
    UNION ALL
    SELECT d.id, CONCAT(d.code, '-IND'), CONCAT(d.code, N' — Minh chứng đóng góp cá nhân'),
           'DOCUMENT', 'INDIVIDUAL', 'HUMAN', 'EACH_STUDENT', 'POL-03'
    FROM dbo.review_gate_definitions d WHERE d.template_id IN (@gateItId, @gateMktId, @gateItMktId)
    UNION ALL
    SELECT d.id, CONCAT('INT-', d.code), CONCAT(d.code, N' — Sản phẩm tích hợp liên ngành IT×MKT'),
           'DOCUMENT', 'INTEGRATION', 'HUMAN', 'PROJECT', 'POL-05'
    FROM dbo.review_gate_definitions d WHERE d.template_id = @gateItMktId
) AS s
ON t.gate_definition_id = s.gate_definition_id AND t.code = s.code
WHEN NOT MATCHED THEN
    INSERT (gate_definition_id, code, title, type, scope, verification, required, target_cardinality, policy_tag)
    VALUES (s.gate_definition_id, s.code, s.title, s.type, s.scope, s.verification, 1, s.target_cardinality, s.policy_tag);

-- 7. Three PUBLISHED AcademicBlueprints (IT_ONLY, MKT_ONLY, IT_MKT)
DECLARE @configIt nvarchar(max) = N'{"gateRules":[{"code":"G1","sequence":1,"title":"Gate 1 — Kiến trúc & Spec","objective":"Thẩm định kiến trúc phần mềm","submissionOffsetWorkingDays":15,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":30,"blocksDefense":false,"requiredDeliverables":["SRS & Architecture"] },{"code":"G2","sequence":2,"title":"Gate 2 — Core Features","objective":"Đánh giá thực thi cốt lõi","submissionOffsetWorkingDays":35,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":false,"requiredDeliverables":["Sprint Demo"] },{"code":"G3","sequence":3,"title":"Gate 3 — Pre-Defense","objective":"Chốt điều kiện bảo vệ","submissionOffsetWorkingDays":55,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":true,"requiredDeliverables":["Release Candidate"] }],"sprintRules":[{"originId":"SP1","name":"Sprint 1 — Foundation","startOffsetWorkingDays":0,"durationWorkingDays":15},{"originId":"SP2","name":"Sprint 2 — Core Build","startOffsetWorkingDays":16,"durationWorkingDays":20},{"originId":"SP3","name":"Sprint 3 — Hardening","startOffsetWorkingDays":37,"durationWorkingDays":18}],"milestoneRules":[{"originId":"MS1","title":"G1 Checkpoint","description":"Hồ sơ G1","startOffsetWorkingDays":0,"dueOffsetWorkingDays":15,"sortOrder":1},{"originId":"MS2","title":"G2 Checkpoint","description":"Hồ sơ G2","startOffsetWorkingDays":16,"dueOffsetWorkingDays":35,"sortOrder":2},{"originId":"MS3","title":"G3 Checkpoint","description":"Hồ sơ G3","startOffsetWorkingDays":36,"dueOffsetWorkingDays":55,"sortOrder":3}],"evaluationSchemePreset":"POL-01"}';

DECLARE @configMkt nvarchar(max) = N'{"gateRules":[{"code":"G1","sequence":1,"title":"Gate 1 — Research & Strategy","objective":"Nghiên cứu thị trường & KPI","submissionOffsetWorkingDays":15,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":30,"blocksDefense":false,"requiredDeliverables":["Market Research"] },{"code":"G2","sequence":2,"title":"Gate 2 — Campaign Execution","objective":"Đo lường chiến dịch & attribution POL-04","submissionOffsetWorkingDays":35,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":false,"requiredDeliverables":["Campaign Report"] },{"code":"G3","sequence":3,"title":"Gate 3 — Growth Audit","objective":"Tổng kết ROI & pre-defense","submissionOffsetWorkingDays":55,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":true,"requiredDeliverables":["Final Growth Dossier"] }],"sprintRules":[{"originId":"SP1","name":"Sprint 1 — Discovery","startOffsetWorkingDays":0,"durationWorkingDays":15},{"originId":"SP2","name":"Sprint 2 — Campaign","startOffsetWorkingDays":16,"durationWorkingDays":20},{"originId":"SP3","name":"Sprint 3 — Optimization","startOffsetWorkingDays":37,"durationWorkingDays":18}],"milestoneRules":[{"originId":"MS1","title":"G1 Strategy","description":"Hồ sơ G1 MKT","startOffsetWorkingDays":0,"dueOffsetWorkingDays":15,"sortOrder":1},{"originId":"MS2","title":"G2 Execution","description":"Hồ sơ G2 MKT","startOffsetWorkingDays":16,"dueOffsetWorkingDays":35,"sortOrder":2},{"originId":"MS3","title":"G3 Audit","description":"Hồ sơ G3 MKT","startOffsetWorkingDays":36,"dueOffsetWorkingDays":55,"sortOrder":3}],"evaluationSchemePreset":"POL-01"}';

DECLARE @configItMkt nvarchar(max) = N'{"gateRules":[{"code":"G1","sequence":1,"title":"Gate 1 — Product & Brand Alignment","objective":"Đồng bộ giải pháp kỹ thuật IT & giá trị MKT","submissionOffsetWorkingDays":15,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":30,"blocksDefense":false,"requiredDeliverables":["Integrated Product Spec"] },{"code":"G2","sequence":2,"title":"Gate 2 — MVP + Funnel Live","objective":"Tracking tích hợp & chiến dịch thử nghiệm","submissionOffsetWorkingDays":35,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":false,"requiredDeliverables":["Integrated Demo"] },{"code":"G3","sequence":3,"title":"Gate 3 — Go-to-Market Readiness","objective":"Nghiệm thu liên ngành trước Hội đồng","submissionOffsetWorkingDays":55,"reviewDurationWorkingDays":5,"revisionDurationWorkingDays":5,"weight":35,"blocksDefense":true,"requiredDeliverables":["Integrated Final Package"] }],"sprintRules":[{"originId":"SP1","name":"Sprint 1 — Alignment","startOffsetWorkingDays":0,"durationWorkingDays":15},{"originId":"SP2","name":"Sprint 2 — Integration","startOffsetWorkingDays":16,"durationWorkingDays":20},{"originId":"SP3","name":"Sprint 3 — Launch Prep","startOffsetWorkingDays":37,"durationWorkingDays":18}],"milestoneRules":[{"originId":"MS1","title":"G1 Alignment","description":"Tích hợp G1","startOffsetWorkingDays":0,"dueOffsetWorkingDays":15,"sortOrder":1},{"originId":"MS2","title":"G2 Integration","description":"Tích hợp G2","startOffsetWorkingDays":16,"dueOffsetWorkingDays":35,"sortOrder":2},{"originId":"MS3","title":"G3 Launch Prep","description":"Tích hợp G3","startOffsetWorkingDays":36,"dueOffsetWorkingDays":55,"sortOrder":3}],"integrationDeliverables":[{"code":"INT-G1-ARCH","title":"Hồ sơ tích hợp Kiến trúc sản phẩm & Định vị MKT (POL-05)","stages":["G1","FINAL"]},{"code":"INT-G3-GO2MARKET","title":"Báo cáo tích hợp MVP & Hiệu quả chiến dịch (POL-05)","stages":["G2","G3","FINAL"]}],"evaluationSchemePreset":"POL-01"}';

MERGE dbo.academic_blueprints AS t
USING (VALUES
    (@semesterId, 'IT_ONLY', 'BP-PILOT-IT-ONLY', N'Blueprint Thí điểm Đơn ngành IT', 1, 'PUBLISHED', @deptItId, NULL, @policyVerId, @gateItId, @msTplId, @rubItId, @configIt, @actorId),
    (@semesterId, 'MKT_ONLY', 'BP-PILOT-MKT-ONLY', N'Blueprint Thí điểm Đơn ngành Marketing', 1, 'PUBLISHED', @deptMktId, NULL, @policyVerId, @gateMktId, @msTplId, @rubMktId, @configMkt, @actorId),
    (@semesterId, 'IT_MKT', 'BP-PILOT-IT-MKT', N'Blueprint Thí điểm Liên ngành IT × Marketing', 1, 'PUBLISHED', @deptItId, @deptMktId, @policyVerId, @gateItMktId, @msTplId, @rubItMktId, @configItMkt, @actorId)
) AS s(academic_semester_id, track_code, code, name, version, status, lead_department_id, participating_department_id, period_policy_version_id, gate_template_id, milestone_template_id, rubric_id, config_json, created_by)
ON t.academic_semester_id = s.academic_semester_id AND t.code = s.code AND t.version = s.version
WHEN NOT MATCHED THEN
    INSERT (academic_semester_id, track_code, code, name, version, status, lead_department_id, participating_department_id, period_policy_version_id, gate_template_id, milestone_template_id, rubric_id, config_json, concurrency_token, created_by, published_at)
    VALUES (s.academic_semester_id, s.track_code, s.code, s.name, s.version, s.status, s.lead_department_id, s.participating_department_id, s.period_policy_version_id, s.gate_template_id, s.milestone_template_id, s.rubric_id, s.config_json, NEWID(), s.created_by, SYSUTCDATETIME());

DECLARE @bpItId bigint = (SELECT id FROM dbo.academic_blueprints WHERE academic_semester_id = @semesterId AND code = 'BP-PILOT-IT-ONLY' AND version = 1);
DECLARE @bpMktId bigint = (SELECT id FROM dbo.academic_blueprints WHERE academic_semester_id = @semesterId AND code = 'BP-PILOT-MKT-ONLY' AND version = 1);
DECLARE @bpItMktId bigint = (SELECT id FROM dbo.academic_blueprints WHERE academic_semester_id = @semesterId AND code = 'BP-PILOT-IT-MKT' AND version = 1);

MERGE dbo.academic_blueprint_approvals AS t
USING (VALUES
    (@bpItId, @deptItId, 'LEAD', 'APPROVED', @actorId, N'Pilot configuration pre-approved'),
    (@bpMktId, @deptMktId, 'LEAD', 'APPROVED', @actorId, N'Pilot configuration pre-approved'),
    (@bpItMktId, @deptItId, 'LEAD', 'APPROVED', @actorId, N'Lead IT pre-approved'),
    (@bpItMktId, @deptMktId, 'PARTICIPATING', 'APPROVED', @actorId, N'Participating MKT pre-approved')
) AS s(blueprint_id, department_id, scope_role, decision, decided_by, notes)
ON t.blueprint_id = s.blueprint_id AND t.department_id = s.department_id
WHEN NOT MATCHED THEN
    INSERT (blueprint_id, department_id, scope_role, decision, decided_by, decided_at, notes)
    VALUES (s.blueprint_id, s.department_id, s.scope_role, s.decision, s.decided_by, SYSUTCDATETIME(), s.notes);

COMMIT TRANSACTION;
