-- Staging-only additive fixture for P1-REVIEW-IT (StudySpace).
-- First upload db/demo/studyspace-report.pdf through IFileStorage with the
-- 32-hex key below. Never run on a real student project.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
DECLARE @p bigint, @student bigint, @major bigint, @department bigint, @semester bigint, @staff bigint;
SELECT @p=p.id,@semester=t.academic_semester_id FROM projects p JOIN teams t ON t.id=p.team_id
WHERE p.code='P1-REVIEW-IT' AND p.title LIKE '%StudySpace%' AND p.status='ACTIVE';
SELECT @student=u.id,@major=u.major_id,@department=u.department_id FROM users u
JOIN team_members tm ON tm.user_id=u.id JOIN projects p ON p.team_id=tm.team_id
WHERE p.id=@p AND u.email='p1.se1@demo.aipms.invalid' AND tm.left_at IS NULL AND tm.is_leader=1;
SELECT TOP(1) @staff=u.id FROM users u JOIN user_roles ur ON ur.user_id=u.id JOIN roles r ON r.id=ur.role_id
WHERE r.code='DEPARTMENT_STAFF' AND u.department_id=@department AND u.email LIKE '%demo.aipms.invalid' ORDER BY u.id;
IF @p IS NULL OR @student IS NULL OR @major IS NULL OR @staff IS NULL
    THROW 51000,'Expected synthetic StudySpace fixture and demo staff are missing.',1;
DECLARE @now datetime2(0)=SYSUTCDATETIME(), @milestone bigint, @rubric bigint, @deliverable bigint, @version bigint;
IF NOT EXISTS(SELECT 1 FROM milestones WHERE project_id=@p AND title='AI DEMO - Technical report milestone')
 INSERT milestones(project_id,title,description,start_date,due_date,status,created_by)
 VALUES(@p,'AI DEMO - Technical report milestone','Synthetic risk signals; no official assessment.',DATEADD(day,-14,@now),DATEADD(day,-1,@now),'IN_PROGRESS',@student);
SELECT @milestone=id FROM milestones WHERE project_id=@p AND title='AI DEMO - Technical report milestone';
INSERT tasks(milestone_id,title,status,priority,start_at,due_at,created_by,created_at,updated_at)
SELECT @milestone,v.title,v.status,'HIGH',DATEADD(day,-14,@now),DATEADD(day,v.dueDays,@now),@student,DATEADD(day,-14,@now),DATEADD(day,-9,@now)
FROM (VALUES('AI DEMO - Complete upload API','IN_PROGRESS',-2),('AI DEMO - Resolve deployment access','BLOCKED',3))v(title,status,dueDays)
WHERE NOT EXISTS(SELECT 1 FROM tasks t WHERE t.milestone_id=@milestone AND t.title=v.title);
INSERT task_disciplines(task_id,major_id,role,created_by)
SELECT t.id,@major,'PRIMARY',@student FROM tasks t WHERE t.milestone_id=@milestone
AND NOT EXISTS(SELECT 1 FROM task_disciplines td WHERE td.task_id=t.id AND td.major_id=@major);
INSERT task_assignees(task_id,user_id,assigned_by)
SELECT t.id,@student,@student FROM tasks t WHERE t.milestone_id=@milestone
AND NOT EXISTS(SELECT 1 FROM task_assignees a WHERE a.task_id=t.id AND a.user_id=@student);
IF NOT EXISTS(SELECT 1 FROM rubrics WHERE code='AI-DEMO-IT-20261101')
BEGIN
 INSERT rubrics(department_id,academic_semester_id,code,name,description,created_by)
 VALUES(@department,@semester,'AI-DEMO-IT-20261101',N'AI DEMO - StudySpace IT',N'Bộ tiêu chí thử nghiệm trợ lý; không phải phương án tính điểm.',@staff);
 SET @rubric=SCOPE_IDENTITY();
 INSERT evaluation_criteria(code,name,description)
 SELECT v.code,v.name,v.description FROM (VALUES
 ('AI-DEMO-ARCH-20261101',N'Architecture and implementation',N'Mô tả module, luồng dữ liệu, sơ đồ triển khai và môi trường vận hành.'),
 ('AI-DEMO-CODE-20261101',N'Code quality',N'Cấu trúc mã, xử lý lỗi, ví dụ mã nguồn và minh chứng code review.'),
 ('AI-DEMO-TEST-20261101',N'Testing and security',N'Test case, kết quả thực thi, kiểm tra phân quyền và minh chứng bảo mật.'))v(code,name,description)
 WHERE NOT EXISTS(SELECT 1 FROM evaluation_criteria c WHERE c.code=v.code);
 INSERT rubric_criteria(rubric_id,criterion_id,weight_percent,max_score,sort_order,is_required)
 SELECT @rubric,c.id,v.weight,10,v.sort,1 FROM (VALUES('AI-DEMO-ARCH-20261101',40,1),('AI-DEMO-CODE-20261101',30,2),('AI-DEMO-TEST-20261101',30,3))v(code,weight,sort)
 JOIN evaluation_criteria c ON c.code=v.code;
 INSERT rubric_versions(rubric_id,root_rubric_id,version_number,status,concurrency_token) VALUES(@rubric,@rubric,1,'PUBLISHED',NEWID());
END;
SELECT @rubric=id FROM rubrics WHERE code='AI-DEMO-IT-20261101';
IF NOT EXISTS(SELECT 1 FROM deliverables WHERE project_id=@p AND title='AI DEMO - StudySpace technical report')
 INSERT deliverables(project_id,milestone_id,title,description,due_at,status,created_by,evidence_scope,target_major_id)
 VALUES(@p,@milestone,'AI DEMO - StudySpace technical report',N'Bản nháp có chủ đích thiếu minh chứng kiểm thử.',DATEADD(day,3,@now),'OPEN',@student,'MAJOR_SPECIFIC',@major);
SELECT @deliverable=id FROM deliverables WHERE project_id=@p AND title='AI DEMO - StudySpace technical report';
IF NOT EXISTS(SELECT 1 FROM deliverable_versions WHERE deliverable_id=@deliverable)
 INSERT deliverable_versions(deliverable_id,version_number,submitted_by,submission_note,status) VALUES(@deliverable,1,@student,'AI demo synthetic PDF','SUBMITTED');
SELECT @version=id FROM deliverable_versions WHERE deliverable_id=@deliverable AND version_number=1;
IF NOT EXISTS(SELECT 1 FROM files WHERE deliverable_version_id=@version)
 INSERT files(uploaded_by,deliverable_version_id,original_file_name,storage_path,mime_type,file_size_bytes,checksum_sha256)
 VALUES(@student,@version,'studyspace-report.pdf','ea572be4229daab7b19d85fdcc061fcd','application/pdf',1493,'ea572be4229daab7b19d85fdcc061fcd62f53faf82a2cb2cd7b4bf3465860470');
IF NOT EXISTS(SELECT 1 FROM deliverables WHERE project_id=@p AND title='AI DEMO - Testing evidence pending')
 INSERT deliverables(project_id,milestone_id,title,due_at,status,created_by,evidence_scope,target_major_id)
 VALUES(@p,@milestone,'AI DEMO - Testing evidence pending',DATEADD(day,3,@now),'OPEN',@student,'MAJOR_SPECIFIC',@major);
DECLARE @period bigint,@template bigint,@package bigint;
IF NOT EXISTS(SELECT 1 FROM project_periods WHERE code='AI-DEMO-FINAL-20261101')
 INSERT project_periods(academic_semester_id,code,name,period_type,status,start_at,end_at)
 VALUES(@semester,'AI-DEMO-FINAL-20261101','AI DEMO - StudySpace COLD','FINAL_SUBMISSION','ACTIVE',DATEADD(day,-1,@now),DATEADD(day,30,@now));
SELECT @period=id FROM project_periods WHERE code='AI-DEMO-FINAL-20261101';
IF EXISTS(SELECT 1 FROM cold_packages WHERE project_id=@p AND project_period_id<>@period)
 THROW 51000,'Existing non-demo COLD package must not be modified.',1;
IF NOT EXISTS(SELECT 1 FROM cold_report_templates WHERE project_period_id=@period AND major_id=@major AND version=1)
 INSERT cold_report_templates(major_id,project_period_id,version,name,reports_json,created_by,created_at)
 VALUES(@major,@period,1,'AI DEMO - Technical checklist',N'[{"ReportType":"SOFTWARE_DESIGN","Name":"Architecture and implementation","Required":true},{"ReportType":"IMPLEMENTATION","Name":"Code quality","Required":true},{"ReportType":"TESTING","Name":"Testing and security","Required":true}]',@staff,@now);
SELECT @template=id FROM cold_report_templates WHERE project_period_id=@period AND major_id=@major AND version=1;
IF NOT EXISTS(SELECT 1 FROM cold_packages WHERE project_id=@p)
 INSERT cold_packages(project_id,project_period_id,deadline,status,registration_snapshot_json,created_by,created_at)
 SELECT @p,@period,DATEADD(day,30,@now),'OPEN',snapshot_json,@staff,@now FROM project_registration_snapshots
 WHERE id=(SELECT MAX(id) FROM project_registration_snapshots WHERE project_id=@p);
SELECT @package=id FROM cold_packages WHERE project_id=@p;
INSERT cold_checklist_items(package_id,template_id,template_version,report_type,name,required,scope,major_id)
SELECT @package,@template,1,v.code,v.name,1,'MAJOR_SPECIFIC',@major
FROM (VALUES('SOFTWARE_DESIGN','Architecture and implementation'),('IMPLEMENTATION','Code quality'),('TESTING','Testing and security'))v(code,name)
WHERE NOT EXISTS(SELECT 1 FROM cold_checklist_items ci WHERE ci.package_id=@package AND ci.report_type=v.code AND ci.major_id=@major);
COMMIT;
SELECT @p project_id,@student student_id,@version deliverable_version_id,@rubric rubric_id,@package checklist_package_id;
