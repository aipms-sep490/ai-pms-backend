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

    IF COL_LENGTH(N'dbo.ai_setup_applications', N'scope_type') < 32
    BEGIN
        ALTER TABLE dbo.ai_setup_applications ALTER COLUMN scope_type varchar(32) NOT NULL;
    END;

    ALTER TABLE dbo.ai_setup_applications
        ADD CONSTRAINT ck_ai_setup_applications_scope
        CHECK (scope_type IN (
            'PROJECT',
            'RUBRIC',
            'REVIEW_GATE',
            'COLD_TEMPLATE',
            'TOPIC_BATCH',
            'KNOWLEDGE_DOC',
            'MILESTONE_TEMPLATE',
            'SEMESTER_ROLLOVER'
        ));
END;

COMMIT TRANSACTION;
