/* AI-PMS initial seed data */
USE [AI_PMS];
GO

SET NOCOUNT ON;
GO

/* Required system roles.
   SUPERVISOR is intentionally NOT seeded as a role.
   A supervisor is modeled as a user (normally a LECTURER) with a supervisor_profiles record.
*/
MERGE dbo.roles AS target
USING (VALUES
    (N'ADMIN',            N'Administrator',      N'System administrator with full platform permissions'),
    (N'DEPARTMENT_STAFF', N'Department Staff',  N'Academic/department staff account'),
    (N'LECTURER',         N'Lecturer',           N'Lecturer account; may also have a supervisor profile'),
    (N'STUDENT',          N'Student',            N'Student account')
) AS source(code, name, description)
ON target.code = source.code
WHEN MATCHED THEN
    UPDATE SET
        name = source.name,
        description = source.description,
        is_system_role = 1,
        updated_at = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (code, name, description, is_system_role)
    VALUES (source.code, source.name, source.description, 1);
GO

/*
 Academic seed data is intentionally omitted because the current requirements
 do not provide an approved organization / department / major / semester master list.
 Add official academic data here after the team confirms it.
*/

PRINT N'AI-PMS required role seed data applied successfully.';
GO
