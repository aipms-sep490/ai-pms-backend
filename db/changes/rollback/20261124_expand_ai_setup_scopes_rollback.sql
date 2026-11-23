SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ai_setup_applications', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE name = N'ck_ai_setup_applications_scope'
          AND parent_object_id = OBJECT_ID(N'dbo.ai_setup_applications')
    )
    BEGIN
        ALTER TABLE dbo.ai_setup_applications DROP CONSTRAINT ck_ai_setup_applications_scope;
    END;

    DELETE FROM dbo.ai_setup_applications
    WHERE scope_type NOT IN ('PROJECT', 'RUBRIC');

    ALTER TABLE dbo.ai_setup_applications ALTER COLUMN scope_type varchar(24) NOT NULL;

    ALTER TABLE dbo.ai_setup_applications
        ADD CONSTRAINT ck_ai_setup_applications_scope
        CHECK (scope_type IN ('PROJECT', 'RUBRIC'));
END;

COMMIT TRANSACTION;
