/* Input-only v5 fixtures. No project approval, assignment, score or result is seeded. */
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
IF DB_NAME() NOT LIKE N'AI_PMS_E2E[_]%' OR NOT EXISTS (
    SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'AIPMS_E2E_OWNER'
    AND CONVERT(nvarchar(100),value)=N'remediation-v1')
    THROW 51000, 'V5 seed requires an owned disposable E2E database.', 1;
DECLARE @password nvarchar(500)=CONVERT(nvarchar(500),SESSION_CONTEXT(N'e2e_password_hash'));
IF @password IS NULL OR LEN(@password)<40 THROW 51000, 'E2E password hash session context is missing.', 1;
BEGIN TRANSACTION;
DECLARE @org bigint=(SELECT id FROM dbo.organizations WHERE code=N'E2E');
IF @org IS NULL THROW 51000, 'Apply the base E2E seed first.', 1;

MERGE dbo.departments AS t USING (VALUES
    (N'V5-IT',N'V5 Information Technology'),(N'V5-MKT',N'V5 Marketing'),(N'V5-DES',N'V5 Design')) s(code,name)
ON t.organization_id=@org AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(organization_id,code,name) VALUES(@org,s.code,s.name);
MERGE dbo.majors AS t USING (
    SELECT id AS department_id,code,name FROM dbo.departments WHERE organization_id=@org AND code IN (N'V5-IT',N'V5-MKT',N'V5-DES')
) s ON t.department_id=s.department_id AND t.code=s.code
WHEN NOT MATCHED THEN INSERT(department_id,code,name) VALUES(s.department_id,s.code,s.name);

DECLARE @actors TABLE(email nvarchar(255),full_name nvarchar(200),department_code nvarchar(30),role_code nvarchar(30),student_code nvarchar(50));
INSERT @actors VALUES
('v5.staff.it@e2e.invalid','V5 IT Staff','V5-IT','DEPARTMENT_STAFF',NULL),
('v5.staff.marketing@e2e.invalid','V5 Marketing Staff','V5-MKT','DEPARTMENT_STAFF',NULL),
('v5.staff.design@e2e.invalid','V5 Design Staff','V5-DES','DEPARTMENT_STAFF',NULL),
('v5.primary@e2e.invalid','V5 Primary','V5-IT','LECTURER',NULL),
('v5.mentor.marketing@e2e.invalid','V5 Marketing Mentor','V5-MKT','LECTURER',NULL),
('v5.mentor.design@e2e.invalid','V5 Design Mentor','V5-DES','LECTURER',NULL),
('v5.evaluator.it@e2e.invalid','V5 IT Evaluator','V5-IT','LECTURER',NULL),
('v5.evaluator.marketing@e2e.invalid','V5 Marketing Evaluator','V5-MKT','LECTURER',NULL),
('v5.evaluator.design@e2e.invalid','V5 Design Evaluator','V5-DES','LECTURER',NULL),
('v5.a.leader@e2e.invalid','V5 A IT Leader','V5-IT','STUDENT','V5-A-001'),
('v5.a.member1@e2e.invalid','V5 A IT Member 1','V5-IT','STUDENT','V5-A-002'),
('v5.a.member2@e2e.invalid','V5 A IT Member 2','V5-IT','STUDENT','V5-A-003'),
('v5.b.leader@e2e.invalid','V5 B Marketing Leader','V5-MKT','STUDENT','V5-B-001'),
('v5.b.member1@e2e.invalid','V5 B Marketing Member 1','V5-MKT','STUDENT','V5-B-002'),
('v5.b.member2@e2e.invalid','V5 B Marketing Member 2','V5-MKT','STUDENT','V5-B-003'),
('v5.c.leader@e2e.invalid','V5 C IT Leader','V5-IT','STUDENT','V5-C-001'),
('v5.c.marketing@e2e.invalid','V5 C Marketing Member','V5-MKT','STUDENT','V5-C-002'),
('v5.c.design@e2e.invalid','V5 C Design Member','V5-DES','STUDENT','V5-C-003');
MERGE dbo.users AS t USING (
    SELECT a.*,d.id AS department_id,m.id AS major_id FROM @actors a
    JOIN dbo.departments d ON d.organization_id=@org AND d.code=a.department_code
    JOIN dbo.majors m ON m.department_id=d.id AND m.code=d.code
) s ON t.email=s.email
WHEN NOT MATCHED THEN INSERT(department_id,major_id,email,password_hash,full_name,student_code,status,academic_profile_status)
    VALUES(s.department_id,CASE WHEN s.role_code='STUDENT' THEN s.major_id END,s.email,@password,s.full_name,s.student_code,'ACTIVE','VERIFIED');
INSERT dbo.user_roles(user_id,role_id)
SELECT u.id,r.id FROM @actors a JOIN dbo.users u ON u.email=a.email JOIN dbo.roles r ON r.code=a.role_code
WHERE NOT EXISTS(SELECT 1 FROM dbo.user_roles x WHERE x.user_id=u.id AND x.role_id=r.id);
INSERT dbo.supervisor_profiles(user_id,max_active_projects)
SELECT u.id,10 FROM @actors a JOIN dbo.users u ON u.email=a.email WHERE a.role_code='LECTURER'
AND NOT EXISTS(SELECT 1 FROM dbo.supervisor_profiles p WHERE p.user_id=u.id);
INSERT dbo.e2e_aliases(alias_name,entity_type,entity_id)
SELECT CONCAT('v5-user-',LEFT(a.email,CHARINDEX('@',a.email)-1)),'user',u.id FROM @actors a JOIN dbo.users u ON u.email=a.email
WHERE NOT EXISTS(SELECT 1 FROM dbo.e2e_aliases x WHERE x.entity_type='user' AND x.entity_id=u.id);
INSERT dbo.e2e_aliases(alias_name,entity_type,entity_id)
SELECT CONCAT('v5-major-',m.code),'major',m.id FROM dbo.majors m JOIN dbo.departments d ON d.id=m.department_id
WHERE d.organization_id=@org AND m.code IN ('V5-IT','V5-MKT','V5-DES')
AND NOT EXISTS(SELECT 1 FROM dbo.e2e_aliases x WHERE x.entity_type='major' AND x.entity_id=m.id);
COMMIT;
