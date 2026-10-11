SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- These codes are the actual ASP.NET authorization policies, not academic grants.
-- Role-permission rows cannot override an assignment or frozen resource scope.
INSERT dbo.permissions(code,name,description,is_system_permission)
SELECT v.code,v.name,v.description,1
FROM (VALUES
    (N'AdminOnly',N'Platform administration',N'Admin-only policy; academic publication and scoring still require resource authority.'),
    (N'AcademicManagement',N'Academic management entry',N'Admin or department staff entry policy; frozen department/component scope is checked by the workflow.'),
    (N'LecturerOnly',N'Lecturer entry',N'Lecturer role entry policy; active assignment and resource scope remain required.'),
    (N'StudentOnly',N'Student entry',N'Student role entry policy; ownership, project membership and state remain required.'),
    (N'ProjectAccess',N'Project access',N'Project resource access policy; does not grant academic mutation authority.'),
    (N'AccountSecurityManagement',N'Account security administration',N'Admin account/security policy; no evaluator or publication authority implied.')
) v(code,name,description)
WHERE NOT EXISTS(SELECT 1 FROM dbo.permissions p WITH (UPDLOCK,HOLDLOCK) WHERE p.code=v.code);

COMMIT;
