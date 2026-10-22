/* Additive, rerunnable staging demo fixture. Never use for production data. */
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF DB_NAME() <> N'AI_PMS'
    THROW 51000, 'This fixture must run only against AI_PMS staging.', 1;

BEGIN TRANSACTION;
DECLARE @now datetime2(0) = SYSUTCDATETIME();
DECLARE @org bigint = (SELECT id FROM dbo.organizations WHERE code = N'TEST-ORG');
DECLARE @semester bigint = (SELECT id FROM dbo.academic_semesters WHERE organization_id = @org AND code = N'FE-ACCEPT-FA26');
DECLARE @period bigint = (SELECT id FROM dbo.project_periods WHERE academic_semester_id = @semester AND code = N'FE-ACCEPT-EVAL-FA26');
DECLARE @itDept bigint = (SELECT id FROM dbo.departments WHERE code = N'TEST-IT');
DECLARE @mktDept bigint = (SELECT id FROM dbo.departments WHERE code = N'TEST-BA');
DECLARE @designDept bigint = (SELECT id FROM dbo.departments WHERE code = N'TEST-DESIGN');
DECLARE @itMajor bigint = (SELECT id FROM dbo.majors WHERE code = N'TEST-SE');
DECLARE @mktMajor bigint = (SELECT id FROM dbo.majors WHERE code = N'TEST-MKT');
DECLARE @designMajor bigint = (SELECT id FROM dbo.majors WHERE code = N'TEST-DESIGN');
DECLARE @staffIt bigint = (SELECT id FROM dbo.users WHERE email = N'staff.it@demo.aipms.invalid');
DECLARE @staffMkt bigint = (SELECT id FROM dbo.users WHERE email = N'staff.ba@demo.aipms.invalid');
DECLARE @staffDesign bigint = (SELECT id FROM dbo.users WHERE email = N'staff.design@demo.aipms.invalid');
DECLARE @p1ReviewerCommon bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.it1@demo.aipms.invalid');
DECLARE @p1ReviewerMajor bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.it2@demo.aipms.invalid');
DECLARE @p1ReviewerIndividual bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.it3@demo.aipms.invalid');
DECLARE @p2ReviewerCommon bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.ba1@demo.aipms.invalid');
DECLARE @p2ReviewerMajor bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.ba2@demo.aipms.invalid');
DECLARE @p2ReviewerIndividual bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.ba3@demo.aipms.invalid');
DECLARE @p3ItReviewer bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.it4@demo.aipms.invalid');
DECLARE @p3MktReviewer bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.ba4@demo.aipms.invalid');
DECLARE @p3DesignReviewer bigint = (SELECT id FROM dbo.users WHERE email = N'lecturer.design1@demo.aipms.invalid');
DECLARE @p1Leader bigint = (SELECT id FROM dbo.users WHERE email = N'p1.se1@demo.aipms.invalid');
DECLARE @p2Leader bigint = (SELECT id FROM dbo.users WHERE email = N'p2.mkt1@demo.aipms.invalid');
DECLARE @p3Leader bigint = (SELECT id FROM dbo.users WHERE email = N'p3.se1@demo.aipms.invalid');

IF @org IS NULL OR @semester IS NULL OR @period IS NULL OR @itDept IS NULL OR @mktDept IS NULL OR @designDept IS NULL
    OR @itMajor IS NULL OR @mktMajor IS NULL OR @designMajor IS NULL
    THROW 51001, 'TEST academic catalog is missing.', 1;

/* Teams and frozen academic scope. */
DECLARE @p1Team bigint = (SELECT id FROM dbo.teams WHERE code = N'P1-REVIEW-IT');
IF @p1Team IS NULL
BEGIN
    INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
    VALUES(@semester,N'P1-REVIEW-IT',N'P1 Review Gate - IT',N'Single-major IT review-gate fixture',N'LOCKED',@p1Leader,@now,@now);
    SET @p1Team = SCOPE_IDENTITY();
END;
DECLARE @p2Team bigint = (SELECT id FROM dbo.teams WHERE code = N'P2-REVIEW-MKT');
IF @p2Team IS NULL
BEGIN
    INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
    VALUES(@semester,N'P2-REVIEW-MKT',N'P2 Review Gate - Marketing',N'Single-major Marketing review-gate fixture',N'LOCKED',@p2Leader,@now,@now);
    SET @p2Team = SCOPE_IDENTITY();
END;
DECLARE @p3Team bigint = (SELECT id FROM dbo.teams WHERE code = N'P3-REVIEW-3D');
IF @p3Team IS NULL
BEGIN
    INSERT dbo.teams(academic_semester_id,code,name,description,status,created_by,created_at,updated_at)
    VALUES(@semester,N'P3-REVIEW-3D',N'P3 Review Gate - IT + Marketing + Design',N'Interdisciplinary review-gate fixture',N'LOCKED',@p3Leader,@now,@now);
    SET @p3Team = SCOPE_IDENTITY();
END;

INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
SELECT @p1Team,@semester,u.id,CASE WHEN u.id=@p1Leader THEN 1 ELSE 0 END,@now,@now,@now
FROM dbo.users u WHERE u.email IN (N'p1.se1@demo.aipms.invalid',N'p1.se2@demo.aipms.invalid',N'p1.se3@demo.aipms.invalid',N'p1.se4@demo.aipms.invalid')
AND NOT EXISTS (SELECT 1 FROM dbo.team_members x WHERE x.team_id=@p1Team AND x.user_id=u.id AND x.left_at IS NULL);
INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
SELECT @p2Team,@semester,u.id,CASE WHEN u.id=@p2Leader THEN 1 ELSE 0 END,@now,@now,@now
FROM dbo.users u WHERE u.email IN (N'p2.mkt1@demo.aipms.invalid',N'p2.mkt2@demo.aipms.invalid',N'p2.mkt3@demo.aipms.invalid',N'p2.mkt4@demo.aipms.invalid')
AND NOT EXISTS (SELECT 1 FROM dbo.team_members x WHERE x.team_id=@p2Team AND x.user_id=u.id AND x.left_at IS NULL);
INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader,joined_at,created_at,updated_at)
SELECT @p3Team,@semester,u.id,CASE WHEN u.id=@p3Leader THEN 1 ELSE 0 END,@now,@now,@now
FROM dbo.users u WHERE u.email IN (N'p3.se1@demo.aipms.invalid',N'p3.se2@demo.aipms.invalid',N'p3.mkt1@demo.aipms.invalid',N'p3.mkt2@demo.aipms.invalid',N'p3.design1@demo.aipms.invalid')
AND NOT EXISTS (SELECT 1 FROM dbo.team_members x WHERE x.team_id=@p3Team AND x.user_id=u.id AND x.left_at IS NULL);

INSERT dbo.team_academic_configurations(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token,responsibility_version)
SELECT @p1Team,N'SINGLE_MAJOR',@itMajor,@itDept,NEWID(),NEWID() WHERE NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id=@p1Team);
INSERT dbo.team_academic_configurations(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token,responsibility_version)
SELECT @p2Team,N'SINGLE_MAJOR',@mktMajor,@mktDept,NEWID(),NEWID() WHERE NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id=@p2Team);
INSERT dbo.team_academic_configurations(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token,responsibility_version)
SELECT @p3Team,N'INTERDISCIPLINARY',NULL,@itDept,NEWID(),NEWID() WHERE NOT EXISTS (SELECT 1 FROM dbo.team_academic_configurations WHERE team_id=@p3Team);
INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
SELECT @p1Team,@itMajor,1,4,N'Kỹ thuật phần mềm' WHERE NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id=@p1Team AND major_id=@itMajor);
INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
SELECT @p2Team,@mktMajor,1,4,N'Phân tích marketing' WHERE NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id=@p2Team AND major_id=@mktMajor);
INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
SELECT @p3Team,v.major_id,1,3,v.responsibility FROM (VALUES(@itMajor,N'Kỹ thuật nền tảng'),(@mktMajor,N'Nghiên cứu thị trường'),(@designMajor,N'Thiết kế trải nghiệm')) v(major_id,responsibility)
WHERE NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements x WHERE x.team_id=@p3Team AND x.major_id=v.major_id);

/* Projects and frozen registration snapshots. */
DECLARE @p1Project bigint = (SELECT id FROM dbo.projects WHERE code=N'P1-REVIEW-IT');
IF @p1Project IS NULL BEGIN INSERT dbo.projects(team_id,code,title,description,objectives,status,registered_at,approved_at,created_by,created_at,updated_at,proposal_source,milestones_initialized) VALUES(@p1Team,N'P1-REVIEW-IT',N'P1 Review Gate - StudySpace IT',N'P1 single-major review gate demo',N'Chạy đầy đủ cổng tiến độ IT',N'ACTIVE',@now,@now,@p1Leader,@now,@now,N'STUDENT_PROPOSAL',0); SET @p1Project=SCOPE_IDENTITY(); END;
DECLARE @p2Project bigint = (SELECT id FROM dbo.projects WHERE code=N'P2-REVIEW-MKT');
IF @p2Project IS NULL BEGIN INSERT dbo.projects(team_id,code,title,description,objectives,status,registered_at,approved_at,created_by,created_at,updated_at,proposal_source,milestones_initialized) VALUES(@p2Team,N'P2-REVIEW-MKT',N'P2 Review Gate - StudySpace Marketing',N'P2 single-major review gate demo',N'Chạy đầy đủ cổng tiến độ Marketing',N'ACTIVE',@now,@now,@p2Leader,@now,@now,N'STUDENT_PROPOSAL',0); SET @p2Project=SCOPE_IDENTITY(); END;
DECLARE @p3Project bigint = (SELECT id FROM dbo.projects WHERE code=N'P3-REVIEW-3D');
IF @p3Project IS NULL BEGIN INSERT dbo.projects(team_id,code,title,description,objectives,status,registered_at,approved_at,created_by,created_at,updated_at,proposal_source,milestones_initialized) VALUES(@p3Team,N'P3-REVIEW-3D',N'P3 Review Gate - StudySpace 3 ngành',N'P3 interdisciplinary IT + Marketing + Design review gate demo',N'Đối chiếu scope chung và từng ngành',N'ACTIVE',@now,@now,@p3Leader,@now,@now,N'STUDENT_PROPOSAL',0); SET @p3Project=SCOPE_IDENTITY(); END;
INSERT dbo.project_majors(project_id,major_id) SELECT @p1Project,@itMajor WHERE NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id=@p1Project AND major_id=@itMajor);
INSERT dbo.project_majors(project_id,major_id) SELECT @p2Project,@mktMajor WHERE NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id=@p2Project AND major_id=@mktMajor);
INSERT dbo.project_majors(project_id,major_id) SELECT @p3Project,v.major_id FROM (VALUES(@itMajor),(@mktMajor),(@designMajor)) v(major_id) WHERE NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id=@p3Project AND major_id=v.major_id);
INSERT dbo.project_status_history(project_id,old_status,new_status,changed_by,reason,changed_at)
SELECT p.id,N'DRAFT',N'ACTIVE',p.created_by,N'P1/P2/P3 review-gate staging fixture',@now FROM dbo.projects p WHERE p.id IN (@p1Project,@p2Project,@p3Project)
AND NOT EXISTS (SELECT 1 FROM dbo.project_status_history h WHERE h.project_id=p.id AND h.new_status=N'ACTIVE');

DECLARE @p1Snapshot nvarchar(max)=CONCAT(N'{"Scope":{"ProjectMode":"SINGLE_MAJOR","PrimaryMajorId":',@itMajor,N',"LeadDepartmentId":',@itDept,N',"Requirements":[{"MajorId":',@itMajor,N',"MinMembers":1,"MaxMembers":4,"Responsibility":"Kỹ thuật phần mềm"}]},"Members":[{"UserId":',@p1Leader,N',"IsLeader":true}],"DepartmentIds":[',@itDept,N'],"MajorDepartmentIds":{"',@itMajor,N'":',@itDept,N'}}');
DECLARE @p2Snapshot nvarchar(max)=CONCAT(N'{"Scope":{"ProjectMode":"SINGLE_MAJOR","PrimaryMajorId":',@mktMajor,N',"LeadDepartmentId":',@mktDept,N',"Requirements":[{"MajorId":',@mktMajor,N',"MinMembers":1,"MaxMembers":4,"Responsibility":"Phân tích marketing"}]},"Members":[{"UserId":',@p2Leader,N',"IsLeader":true}],"DepartmentIds":[',@mktDept,N'],"MajorDepartmentIds":{"',@mktMajor,N'":',@mktDept,N'}}');
DECLARE @p3Snapshot nvarchar(max)=CONCAT(N'{"Scope":{"ProjectMode":"INTERDISCIPLINARY","LeadDepartmentId":',@itDept,N',"Requirements":[{"MajorId":',@itMajor,N',"MinMembers":1,"MaxMembers":3},{"MajorId":',@mktMajor,N',"MinMembers":1,"MaxMembers":3},{"MajorId":',@designMajor,N',"MinMembers":1,"MaxMembers":3}]},"Members":[{"UserId":',@p3Leader,N',"IsLeader":true}],"DepartmentIds":[',@itDept,N',',@mktDept,N',',@designDept,N'],"MajorDepartmentIds":{"',@itMajor,N'":',@itDept,N',"',@mktMajor,N'":',@mktDept,N',"',@designMajor,N'":',@designDept,N'}}');
INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json) SELECT @p1Project,@period,@itDept,@p1Leader,@now,@p1Snapshot WHERE NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@p1Project);
INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json) SELECT @p2Project,@period,@mktDept,@p2Leader,@now,@p2Snapshot WHERE NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@p2Project);
INSERT dbo.project_registration_snapshots(project_id,project_period_id,lead_department_id,submitted_by,submitted_at,snapshot_json) SELECT @p3Project,@period,@itDept,@p3Leader,@now,@p3Snapshot WHERE NOT EXISTS (SELECT 1 FROM dbo.project_registration_snapshots WHERE project_id=@p3Project);
DECLARE @p3Snap bigint=(SELECT TOP 1 id FROM dbo.project_registration_snapshots WHERE project_id=@p3Project ORDER BY id DESC);
INSERT dbo.project_department_decisions(snapshot_id,department_id,decision,decided_by,decided_at,reason)
SELECT @p3Snap,v.department_id,N'APPROVED',v.actor,@now,N'P3 staging scope approval' FROM (VALUES(@itDept,@staffIt),(@mktDept,@staffMkt),(@designDept,@staffDesign)) v(department_id,actor)
WHERE NOT EXISTS (SELECT 1 FROM dbo.project_department_decisions d WHERE d.snapshot_id=@p3Snap AND d.department_id=v.department_id);

/* Published templates: one exact single-major template per major and one interdisciplinary template. */
DECLARE @tP1 bigint=(SELECT id FROM dbo.review_gate_templates WHERE code=N'P1-REVIEW-GATE' AND version=1);
IF @tP1 IS NULL BEGIN INSERT dbo.review_gate_templates(project_period_id,code,name,mode,major,status,version,created_by,created_at,updated_at) VALUES(@period,N'P1-REVIEW-GATE',N'P1 Review Gates - IT',N'SINGLE_MAJOR',N'TEST-SE',N'PUBLISHED',1,@staffIt,@now,@now); SET @tP1=SCOPE_IDENTITY(); END;
DECLARE @tP2 bigint=(SELECT id FROM dbo.review_gate_templates WHERE code=N'P2-REVIEW-GATE' AND version=1);
IF @tP2 IS NULL BEGIN INSERT dbo.review_gate_templates(project_period_id,code,name,mode,major,status,version,created_by,created_at,updated_at) VALUES(@period,N'P2-REVIEW-GATE',N'P2 Review Gates - Marketing',N'SINGLE_MAJOR',N'TEST-MKT',N'PUBLISHED',1,@staffMkt,@now,@now); SET @tP2=SCOPE_IDENTITY(); END;
DECLARE @tP3 bigint=(SELECT id FROM dbo.review_gate_templates WHERE code=N'P3-REVIEW-GATE' AND version=1);
IF @tP3 IS NULL BEGIN INSERT dbo.review_gate_templates(project_period_id,code,name,mode,major,status,version,created_by,created_at,updated_at) VALUES(@period,N'P3-REVIEW-GATE',N'P3 Review Gates - IT + Marketing + Design',N'INTERDISCIPLINARY',N'INTERDISCIPLINARY',N'PUBLISHED',1,@staffIt,@now,@now); SET @tP3=SCOPE_IDENTITY(); END;

/* Definitions and requirements are created once per template. */
DECLARE @template bigint, @major bigint, @isInter bit;
DECLARE template_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT id,major,CASE WHEN id=@tP3 THEN 1 ELSE 0 END FROM (VALUES(@tP1,@itMajor),(@tP2,@mktMajor),(@tP3,NULL)) x(id,major);
OPEN template_cursor; FETCH NEXT FROM template_cursor INTO @template,@major,@isInter;
WHILE @@FETCH_STATUS=0
BEGIN
    INSERT dbo.review_gate_definitions(template_id,code,sequence,title,objective,required,block_next_gate,blocks_defense,reviewer_policy,review_scope,weight)
    SELECT @template,N'G1',1,N'Kiểm tra tiến độ',N'Xác nhận kế hoạch và minh chứng tiến độ ban đầu',1,1,0,N'ASSIGNED',N'COMMON',30 WHERE NOT EXISTS (SELECT 1 FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G1');
    INSERT dbo.review_gate_definitions(template_id,code,sequence,title,objective,required,block_next_gate,blocks_defense,reviewer_policy,review_scope,weight)
    SELECT @template,N'G2',2,N'Rà soát chuyên môn',N'Rà soát kết quả theo ngành',1,1,0,N'ASSIGNED',N'MAJOR_SPECIFIC',40 WHERE NOT EXISTS (SELECT 1 FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G2');
    INSERT dbo.review_gate_definitions(template_id,code,sequence,title,objective,required,block_next_gate,blocks_defense,reviewer_policy,review_scope,weight)
    SELECT @template,N'G3',3,N'Bàn giao cuối kỳ',N'Xác nhận hồ sơ bàn giao và phần đóng góp cá nhân',1,0,1,N'ASSIGNED',N'INDIVIDUAL',30 WHERE NOT EXISTS (SELECT 1 FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G3');
    DECLARE @g1 bigint=(SELECT id FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G1');
    DECLARE @g2 bigint=(SELECT id FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G2');
    DECLARE @g3 bigint=(SELECT id FROM dbo.review_gate_definitions WHERE template_id=@template AND code=N'G3');
    INSERT dbo.review_gate_requirements(gate_definition_id,code,title,description,type,scope,major_id,verification,required)
    SELECT @g1,N'PLAN',N'Kế hoạch tiến độ',N'Kế hoạch sprint và mốc kiểm tra',N'LINK',N'COMMON',NULL,N'REVIEW',1 WHERE NOT EXISTS (SELECT 1 FROM dbo.review_gate_requirements WHERE gate_definition_id=@g1 AND code=N'PLAN');
    INSERT dbo.review_gate_requirements(gate_definition_id,code,title,description,type,scope,major_id,verification,required)
    SELECT @g2,CONCAT(N'MAJOR_',@major),N'Báo cáo chuyên môn',N'Minh chứng theo ngành',N'LINK',N'MAJOR_SPECIFIC',@major,N'REVIEW',1 WHERE @isInter=0 AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_requirements WHERE gate_definition_id=@g2 AND code=CONCAT(N'MAJOR_',@major));
    IF @isInter=1
    BEGIN
        INSERT dbo.review_gate_requirements(gate_definition_id,code,title,description,type,scope,major_id,verification,required)
        SELECT @g2,v.code,v.title,N'Minh chứng theo ngành',N'LINK',N'MAJOR_SPECIFIC',v.major_id,N'REVIEW',1 FROM (VALUES(CONCAT(N'MAJOR_',@itMajor),N'IT - Báo cáo kỹ thuật',@itMajor),(CONCAT(N'MAJOR_',@mktMajor),N'Marketing - Báo cáo thị trường',@mktMajor),(CONCAT(N'MAJOR_',@designMajor),N'Design - Bộ thiết kế trải nghiệm',@designMajor)) v(code,title,major_id)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.review_gate_requirements r WHERE r.gate_definition_id=@g2 AND r.code=v.code);
    END;
    FETCH NEXT FROM template_cursor INTO @template,@major,@isInter;
END;
CLOSE template_cursor; DEALLOCATE template_cursor;

/* Materialize gate instances, requirements, and reviewer assignments. */
DECLARE @project bigint, @templateForProject bigint, @leader bigint, @projectMajor bigint, @team bigint;
DECLARE project_cursor CURSOR LOCAL FAST_FORWARD FOR SELECT @p1Project,@tP1,@p1Leader,@itMajor,@p1Team UNION ALL SELECT @p2Project,@tP2,@p2Leader,@mktMajor,@p2Team UNION ALL SELECT @p3Project,@tP3,@p3Leader,NULL,@p3Team;
OPEN project_cursor; FETCH NEXT FROM project_cursor INTO @project,@templateForProject,@leader,@projectMajor,@team;
WHILE @@FETCH_STATUS=0
BEGIN
    INSERT dbo.project_gate_instances(project_id,template_id,template_version,code,sequence,title,objective,status,review_eligibility,required,block_next_gate,blocks_defense,concurrency_token,created_at,weight)
    SELECT @project,@templateForProject,1,d.code,d.sequence,d.title,d.objective,CASE WHEN d.code=N'G1' THEN N'IN_REVIEW' WHEN d.code=N'G2' THEN N'PLANNED' ELSE N'LOCKED' END,CASE WHEN d.code IN (N'G1',N'G2') THEN N'READY' ELSE N'BLOCKED' END,d.required,d.block_next_gate,d.blocks_defense,NEWID(),@now,d.weight
    FROM dbo.review_gate_definitions d WHERE d.template_id=@templateForProject AND NOT EXISTS (SELECT 1 FROM dbo.project_gate_instances i WHERE i.project_id=@project AND i.code=d.code);
    INSERT dbo.project_gate_requirements(gate_instance_id,code,title,type,scope,major_id,target_student_id,required,verification_status)
    SELECT i.id,r.code,r.title,r.type,r.scope,r.major_id,r.target_student_id,r.required,CASE WHEN i.code=N'G1' THEN N'SUBMITTED' ELSE N'PENDING' END
    FROM dbo.project_gate_instances i JOIN dbo.review_gate_definitions d ON d.template_id=i.template_id AND d.code=i.code JOIN dbo.review_gate_requirements r ON r.gate_definition_id=d.id
    WHERE i.project_id=@project AND NOT EXISTS (SELECT 1 FROM dbo.project_gate_requirements x WHERE x.gate_instance_id=i.id AND x.code=r.code);
    DECLARE @g1i bigint=(SELECT id FROM dbo.project_gate_instances WHERE project_id=@project AND code=N'G1');
    DECLARE @g2i bigint=(SELECT id FROM dbo.project_gate_instances WHERE project_id=@project AND code=N'G2');
    DECLARE @g3i bigint=(SELECT id FROM dbo.project_gate_instances WHERE project_id=@project AND code=N'G3');
    INSERT dbo.project_gate_requirements(gate_instance_id,code,title,type,scope,major_id,target_student_id,required,verification_status)
    SELECT @g3i,CONCAT(N'INDIV_',u.id),CONCAT(N'Đóng góp cá nhân - ',u.full_name),N'LINK',N'INDIVIDUAL',NULL,u.id,1,N'PENDING'
    FROM dbo.team_members tm JOIN dbo.users u ON u.id=tm.user_id
    WHERE tm.team_id=@team AND tm.left_at IS NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.project_gate_requirements x WHERE x.gate_instance_id=@g3i AND x.target_student_id=u.id);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g1i,@p1ReviewerCommon,N'COMMON',NULL,NULL,N'PENDING_REVIEW',@now WHERE @project=@p1Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g1i AND reviewer_id=@p1ReviewerCommon AND scope=N'COMMON');
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g2i,@p1ReviewerMajor,N'MAJOR_SPECIFIC',@itMajor,NULL,N'PENDING_REVIEW',@now WHERE @project=@p1Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g2i AND reviewer_id=@p1ReviewerMajor AND scope=N'MAJOR_SPECIFIC' AND major_id=@itMajor);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g3i,@p1ReviewerIndividual,N'INDIVIDUAL',NULL,m.user_id,N'PENDING_REVIEW',@now FROM dbo.team_members m WHERE @project=@p1Project AND m.team_id=@p1Team AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g3i AND reviewer_id=@p1ReviewerIndividual AND scope=N'INDIVIDUAL' AND student_id=m.user_id);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g1i,@p2ReviewerCommon,N'COMMON',NULL,NULL,N'PENDING_REVIEW',@now WHERE @project=@p2Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g1i AND reviewer_id=@p2ReviewerCommon AND scope=N'COMMON');
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g2i,@p2ReviewerMajor,N'MAJOR_SPECIFIC',@mktMajor,NULL,N'PENDING_REVIEW',@now WHERE @project=@p2Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g2i AND reviewer_id=@p2ReviewerMajor AND scope=N'MAJOR_SPECIFIC' AND major_id=@mktMajor);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g3i,@p2ReviewerIndividual,N'INDIVIDUAL',NULL,m.user_id,N'PENDING_REVIEW',@now FROM dbo.team_members m WHERE @project=@p2Project AND m.team_id=@p2Team AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g3i AND reviewer_id=@p2ReviewerIndividual AND scope=N'INDIVIDUAL' AND student_id=m.user_id);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g1i,@staffIt,N'COMMON',NULL,NULL,N'PENDING_REVIEW',@now WHERE @project=@p3Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g1i AND reviewer_id=@staffIt AND scope=N'COMMON');
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g2i,v.reviewer,N'MAJOR_SPECIFIC',v.major_id,NULL,N'PENDING_REVIEW',@now FROM (VALUES(@p3ItReviewer,@itMajor),(@p3MktReviewer,@mktMajor),(@p3DesignReviewer,@designMajor)) v(reviewer,major_id) WHERE @project=@p3Project AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments WHERE gate_instance_id=@g2i AND reviewer_id=v.reviewer AND scope=N'MAJOR_SPECIFIC' AND major_id=v.major_id);
    INSERT dbo.review_gate_assignments(gate_instance_id,reviewer_id,scope,major_id,student_id,status,created_at)
    SELECT @g3i,CASE WHEN u.major_id=@itMajor THEN @p3ItReviewer WHEN u.major_id=@mktMajor THEN @p3MktReviewer ELSE @p3DesignReviewer END,N'INDIVIDUAL',NULL,u.id,N'PENDING_REVIEW',@now FROM dbo.team_members tm JOIN dbo.users u ON u.id=tm.user_id WHERE @project=@p3Project AND tm.team_id=@p3Team AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_assignments a WHERE a.gate_instance_id=@g3i AND a.scope=N'INDIVIDUAL' AND a.student_id=u.id);
    FETCH NEXT FROM project_cursor INTO @project,@templateForProject,@leader,@projectMajor,@team;
END;
CLOSE project_cursor; DEALLOCATE project_cursor;

/* Seed external-link evidence on the first gate so reviewer queues have a visible artifact. */
INSERT dbo.review_gate_evidence(project_id,requirement_id,version,artifact_name,notes,submitted_by,submitted_at,status,content_type,size_bytes,sha256,storage_key,link_url)
SELECT i.project_id,r.id,1,CONCAT(r.title,N' - demo'),N'Staging fixture evidence',p.created_by,@now,N'SUBMITTED',N'text/uri-list',0,REPLICATE('0',64),N'external-link',CONCAT(N'https://example.com/aipms-demo/',p.code,N'/',r.code)
FROM dbo.project_gate_requirements r JOIN dbo.project_gate_instances i ON i.id=r.gate_instance_id JOIN dbo.projects p ON p.id=i.project_id
WHERE i.code=N'G1' AND r.verification_status=N'SUBMITTED' AND NOT EXISTS (SELECT 1 FROM dbo.review_gate_evidence e WHERE e.requirement_id=r.id AND e.version=1);
UPDATE r SET latest_evidence_id=e.id FROM dbo.project_gate_requirements r JOIN dbo.review_gate_evidence e ON e.requirement_id=r.id AND e.version=1 WHERE r.verification_status=N'SUBMITTED' AND r.latest_evidence_id IS NULL;

COMMIT;

SELECT p.id AS project_id,p.code,p.title,t.id AS team_id
FROM dbo.projects p JOIN dbo.teams t ON t.id=p.team_id WHERE p.code IN (N'P1-REVIEW-IT',N'P2-REVIEW-MKT',N'P3-REVIEW-3D');
SELECT i.id AS gate_id,i.project_id,p.code AS project_code,i.code,i.status,i.review_eligibility,COUNT(DISTINCT a.id) AS assignment_count,COUNT(DISTINCT r.id) AS requirement_count
FROM dbo.project_gate_instances i JOIN dbo.projects p ON p.id=i.project_id LEFT JOIN dbo.review_gate_assignments a ON a.gate_instance_id=i.id LEFT JOIN dbo.project_gate_requirements r ON r.gate_instance_id=i.id
WHERE p.code IN (N'P1-REVIEW-IT',N'P2-REVIEW-MKT',N'P3-REVIEW-3D') GROUP BY i.id,i.project_id,p.code,i.code,i.status,i.review_eligibility,i.sequence ORDER BY p.code,i.sequence;
