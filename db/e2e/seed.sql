/* Disposable E2E fixture. Values are stable by alias, never by identity id. */
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.e2e_aliases', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.e2e_aliases
    (
        alias_name nvarchar(100) NOT NULL CONSTRAINT pk_e2e_aliases PRIMARY KEY,
        entity_type nvarchar(50) NOT NULL,
        entity_id bigint NOT NULL,
        created_at datetime2(0) NOT NULL CONSTRAINT df_e2e_aliases_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_e2e_aliases_entity UNIQUE (entity_type, entity_id)
    );
END;

DECLARE @password nvarchar(500) = CONVERT(nvarchar(500), SESSION_CONTEXT(N'e2e_password_hash'));
IF @password IS NULL OR LEN(@password) < 40 THROW 51000, 'E2E password hash session context is missing.', 1;

MERGE dbo.organizations AS t USING (VALUES (N'E2E',N'E2E Organization')) s(code,name)
ON t.code=s.code WHEN NOT MATCHED THEN INSERT(code,name) VALUES(s.code,s.name);
DECLARE @org bigint=(SELECT id FROM dbo.organizations WHERE code=N'E2E');
MERGE dbo.departments AS t USING (VALUES (N'E2E-CS',N'Computer Science'),(N'E2E-DS',N'Data Science'),(N'E2E-OUT',N'Outside Department')) s(code,name)
ON t.code=s.code AND t.organization_id=@org
WHEN NOT MATCHED THEN INSERT(organization_id,code,name) VALUES(@org,s.code,s.name);
DECLARE @deptCs bigint=(SELECT id FROM dbo.departments WHERE organization_id=@org AND code=N'E2E-CS');
DECLARE @deptDs bigint=(SELECT id FROM dbo.departments WHERE organization_id=@org AND code=N'E2E-DS');
DECLARE @deptOutside bigint=(SELECT id FROM dbo.departments WHERE organization_id=@org AND code=N'E2E-OUT');
MERGE dbo.majors AS t USING (VALUES (@deptCs,N'E2E-SE',N'Software Engineering'),(@deptDs,N'E2E-DS',N'Data Science')) s(department_id,code,name)
ON t.department_id=s.department_id AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(department_id,code,name) VALUES(s.department_id,s.code,s.name);
DECLARE @majorSe bigint=(SELECT id FROM dbo.majors WHERE code=N'E2E-SE' AND department_id=@deptCs);
DECLARE @majorDs bigint=(SELECT id FROM dbo.majors WHERE code=N'E2E-DS' AND department_id=@deptDs);

MERGE dbo.academic_semesters AS t USING (VALUES (N'E2E-2026',N'E2E Semester')) s(code,name)
ON t.organization_id=@org AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(organization_id,code,name,start_date,end_date,status) VALUES(@org,s.code,s.name,'2026-01-01','2026-12-31',N'ACTIVE');
DECLARE @semester bigint=(SELECT id FROM dbo.academic_semesters WHERE organization_id=@org AND code=N'E2E-2026');

MERGE dbo.project_periods AS t USING (VALUES
    (N'E2E-REG',N'REGISTRATION','2026-01-01','2026-02-28',N'CLOSED'),
    (N'E2E-REVIEW',N'PROJECT_REVIEW','2026-03-01','2026-03-14',N'CLOSED'),
    (N'E2E-SUP',N'SUPERVISOR_SELECTION','2026-03-15','2026-03-31',N'CLOSED'),
    (N'E2E-EXEC',N'EXECUTION','2026-04-01','2026-08-31',N'CLOSED'),
    (N'E2E-FINAL',N'FINAL_SUBMISSION','2026-09-01','2026-09-20',N'CLOSED'),
    (N'E2E-EVAL',N'EVALUATION','2026-09-21','2026-10-31',N'ACTIVE')) s(code,period_type,start_at,end_at,status)
ON t.academic_semester_id=@semester AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(academic_semester_id,code,name,period_type,start_at,end_at,status) VALUES(@semester,s.code,s.code,s.period_type,s.start_at,s.end_at,s.status);
DECLARE @evalPeriod bigint=(SELECT id FROM dbo.project_periods WHERE academic_semester_id=@semester AND code=N'E2E-EVAL');

DECLARE @roleAdmin bigint=(SELECT id FROM dbo.roles WHERE code=N'ADMIN');
DECLARE @roleStaff bigint=(SELECT id FROM dbo.roles WHERE code=N'DEPARTMENT_STAFF');
DECLARE @roleLecturer bigint=(SELECT id FROM dbo.roles WHERE code=N'LECTURER');
DECLARE @roleStudent bigint=(SELECT id FROM dbo.roles WHERE code=N'STUDENT');
MERGE dbo.users AS t USING (VALUES
 (N'admin@e2e.invalid',N'E2E Admin',@deptCs,NULL,N'ADMIN-001'),
 (N'staff.cs@e2e.invalid',N'E2E CS Staff',@deptCs,NULL,N'STAFF-CS'),
 (N'staff.ds@e2e.invalid',N'E2E DS Staff',@deptDs,NULL,N'STAFF-DS'),
 (N'supervisor@e2e.invalid',N'E2E Primary Supervisor',@deptCs,NULL,N'LECT-001'),
 (N'mentor.ds@e2e.invalid',N'E2E Discipline Mentor',@deptDs,NULL,N'LECT-002'),
 (N'staff.outside@e2e.invalid',N'E2E Outside Staff',@deptOutside,NULL,N'STAFF-OUT'),
 (N'lecturer.outside@e2e.invalid',N'E2E Outside Lecturer',@deptOutside,NULL,N'LECT-OUT'),
 (N'lecturer.inactive@e2e.invalid',N'E2E Inactive Lecturer',@deptCs,NULL,N'LECT-INACTIVE'),
 (N'evaluator@e2e.invalid',N'E2E Evaluator',@deptCs,NULL,N'LECT-003'),
 (N'single.leader@e2e.invalid',N'E2E Single Leader',@deptCs,@majorSe,N'STU-004'),
 (N'single.member@e2e.invalid',N'E2E Single Member',@deptCs,@majorSe,N'STU-005'),
 (N'single.third@e2e.invalid',N'E2E Single Third',@deptCs,@majorSe,N'STU-006'),
 (N'inter.third@e2e.invalid',N'E2E Inter Third',@deptCs,@majorSe,N'STU-007'),
 (N'leader@e2e.invalid',N'E2E Student Leader',@deptCs,@majorSe,N'STU-001'),
 (N'member@e2e.invalid',N'E2E Student Member',@deptDs,@majorDs,N'STU-002'),
 (N'outsider@e2e.invalid',N'E2E Out of Scope',@deptDs,@majorDs,N'STU-003')) s(email,full_name,department_id,major_id,employee_code)
ON t.email=s.email
WHEN NOT MATCHED THEN INSERT(department_id,major_id,email,password_hash,full_name,employee_code,status,academic_profile_status) VALUES(s.department_id,s.major_id,s.email,@password,s.full_name,s.employee_code,CASE WHEN s.email=N'lecturer.inactive@e2e.invalid' THEN N'INACTIVE' ELSE N'ACTIVE' END,N'VERIFIED');
UPDATE dbo.users SET student_code=REPLACE(employee_code,N'STU-',N'E2E-STU-'),employee_code=NULL WHERE major_id IS NOT NULL AND email LIKE N'%@e2e.invalid' AND student_code IS NULL;

INSERT dbo.user_roles(user_id,role_id)
SELECT u.id,r.role_id FROM (VALUES
 (N'staff.outside@e2e.invalid',@roleStaff),(N'lecturer.outside@e2e.invalid',@roleLecturer),(N'lecturer.inactive@e2e.invalid',@roleLecturer),(N'admin@e2e.invalid',@roleAdmin),(N'staff.cs@e2e.invalid',@roleStaff),(N'staff.ds@e2e.invalid',@roleStaff),
 (N'supervisor@e2e.invalid',@roleLecturer),(N'mentor.ds@e2e.invalid',@roleLecturer),(N'evaluator@e2e.invalid',@roleLecturer),
 (N'single.leader@e2e.invalid',@roleStudent),(N'single.member@e2e.invalid',@roleStudent),(N'single.third@e2e.invalid',@roleStudent),(N'inter.third@e2e.invalid',@roleStudent),(N'leader@e2e.invalid',@roleStudent),(N'member@e2e.invalid',@roleStudent),(N'outsider@e2e.invalid',@roleStudent)) r(email,role_id)
JOIN dbo.users u ON u.email=r.email WHERE NOT EXISTS (SELECT 1 FROM dbo.user_roles x WHERE x.user_id=u.id AND x.role_id=r.role_id);
MERGE dbo.supervisor_profiles AS t USING (SELECT id FROM dbo.users WHERE email=N'supervisor@e2e.invalid') s(user_id)
ON t.user_id=s.user_id WHEN NOT MATCHED THEN INSERT(user_id,max_active_projects) VALUES(s.user_id,10);
MERGE dbo.supervisor_profiles AS t USING (SELECT id FROM dbo.users WHERE email=N'mentor.ds@e2e.invalid') s(user_id)
ON t.user_id=s.user_id WHEN NOT MATCHED THEN INSERT(user_id,max_active_projects) VALUES(s.user_id,10);
DECLARE @sup bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'supervisor@e2e.invalid'));
DECLARE @mentor bigint=(SELECT id FROM dbo.supervisor_profiles WHERE user_id=(SELECT id FROM dbo.users WHERE email=N'mentor.ds@e2e.invalid'));

MERGE dbo.teams AS t USING (VALUES (N'E2E-SINGLE',N'E2E Single Major',N'FORMING'),(N'E2E-INTER',N'E2E Interdisciplinary',N'FORMING')) s(code,name,status)
ON t.academic_semester_id=@semester AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(academic_semester_id,code,name,status,created_by) VALUES(@semester,s.code,s.name,s.status,(SELECT id FROM dbo.users WHERE email=N'admin@e2e.invalid'));
DECLARE @singleTeam bigint=(SELECT id FROM dbo.teams WHERE academic_semester_id=@semester AND code=N'E2E-SINGLE');
DECLARE @interTeam bigint=(SELECT id FROM dbo.teams WHERE academic_semester_id=@semester AND code=N'E2E-INTER');
INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader)
SELECT @singleTeam,@semester,u.id,CASE WHEN u.email=N'single.leader@e2e.invalid' THEN 1 ELSE 0 END FROM dbo.users u WHERE u.email IN (N'single.leader@e2e.invalid',N'single.member@e2e.invalid',N'single.third@e2e.invalid') AND NOT EXISTS (SELECT 1 FROM dbo.team_members m WHERE m.team_id=@singleTeam AND m.user_id=u.id);
INSERT dbo.team_members(team_id,academic_semester_id,user_id,is_leader)
SELECT @interTeam,@semester,u.id,CASE WHEN u.email=N'leader@e2e.invalid' THEN 1 ELSE 0 END FROM dbo.users u WHERE u.email IN (N'leader@e2e.invalid',N'member@e2e.invalid',N'inter.third@e2e.invalid') AND NOT EXISTS (SELECT 1 FROM dbo.team_members m WHERE m.team_id=@interTeam AND m.user_id=u.id);
MERGE dbo.team_academic_configurations AS t USING (VALUES(@singleTeam,'SINGLE_MAJOR',@majorSe,@deptCs),(@interTeam,'INTERDISCIPLINARY',NULL,@deptCs)) s(team_id,project_mode,primary_major_id,lead_department_id)
ON t.team_id=s.team_id WHEN NOT MATCHED THEN INSERT(team_id,project_mode,primary_major_id,lead_department_id,concurrency_token) VALUES(s.team_id,s.project_mode,s.primary_major_id,s.lead_department_id,NEWID());
INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
SELECT @singleTeam,@majorSe,1,5,N'Software engineering responsibility' WHERE NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id=@singleTeam AND major_id=@majorSe);
INSERT dbo.team_major_requirements(team_id,major_id,min_members,max_members,responsibility)
SELECT @interTeam,v.major_id,1,5,v.responsibility FROM (VALUES(@majorSe,N'Engineering'),(@majorDs,N'Data science')) v(major_id,responsibility)
WHERE NOT EXISTS (SELECT 1 FROM dbo.team_major_requirements WHERE team_id=@interTeam AND major_id=v.major_id);

MERGE dbo.projects AS t USING (VALUES
 (N'E2E-SINGLE-P',@singleTeam,N'E2E Single Project',N'DRAFT'),(N'E2E-INTER-P',@interTeam,N'E2E Interdisciplinary Project',N'DRAFT')) s(code,team_id,title,status)
ON t.code=s.code WHEN NOT MATCHED THEN INSERT(team_id,code,title,description,objectives,problem_statement,expected_output,status,created_by) VALUES(s.team_id,s.code,s.title,N'E2E fixture',N'Validate workflow',N'E2E problem',N'E2E output',s.status,(SELECT id FROM dbo.users WHERE email=CASE WHEN s.team_id=@singleTeam THEN N'single.leader@e2e.invalid' ELSE N'leader@e2e.invalid' END));
DECLARE @singleProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-SINGLE-P');
DECLARE @interProject bigint=(SELECT id FROM dbo.projects WHERE code=N'E2E-INTER-P');
INSERT dbo.project_majors(project_id,major_id) SELECT @singleProject,@majorSe WHERE NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id=@singleProject AND major_id=@majorSe);
INSERT dbo.project_majors(project_id,major_id) SELECT @interProject,v.major_id FROM (VALUES(@majorSe),(@majorDs)) v(major_id) WHERE NOT EXISTS (SELECT 1 FROM dbo.project_majors WHERE project_id=@interProject AND major_id=v.major_id);
INSERT dbo.e2e_aliases(alias_name,entity_type,entity_id) SELECT v.alias_name,v.entity_type,v.entity_id FROM (VALUES
 (N'dept-outside',N'department',@deptOutside),(N'org',N'organization',@org),(N'dept-cs',N'department',@deptCs),(N'dept-ds',N'department',@deptDs),(N'major-se',N'major',@majorSe),(N'major-ds',N'major',@majorDs),
 (N'semester',N'semester',@semester),(N'eval-period',N'period',@evalPeriod),(N'single-team',N'team',@singleTeam),(N'inter-team',N'team',@interTeam),
 (N'single-project',N'project',@singleProject),(N'inter-project',N'project',@interProject),(N'supervisor-profile',N'supervisor_profile',@sup),(N'mentor-profile',N'supervisor_profile',@mentor)) v(alias_name,entity_type,entity_id)
WHERE NOT EXISTS (SELECT 1 FROM dbo.e2e_aliases a WHERE a.alias_name=v.alias_name);
COMMIT TRANSACTION;
