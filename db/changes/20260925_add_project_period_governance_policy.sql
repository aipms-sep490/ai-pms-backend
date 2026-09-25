/* Additive, repeatable policy fields for project-period mode/source governance. */
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH(N'dbo.project_periods', N'allowed_project_modes') IS NULL
        ALTER TABLE dbo.project_periods ADD allowed_project_modes varchar(200) NOT NULL
            CONSTRAINT df_project_periods_allowed_modes DEFAULT ('SINGLE_MAJOR,INTERDISCIPLINARY') WITH VALUES;
    IF COL_LENGTH(N'dbo.project_periods', N'allowed_proposal_sources') IS NULL
        ALTER TABLE dbo.project_periods ADD allowed_proposal_sources varchar(200) NOT NULL
            CONSTRAINT df_project_periods_allowed_sources DEFAULT ('PUBLISHED_TOPIC,STUDENT_PROPOSAL') WITH VALUES;
    IF COL_LENGTH(N'dbo.project_periods', N'policy_version') IS NULL
        ALTER TABLE dbo.project_periods ADD policy_version int NOT NULL
            CONSTRAINT df_project_periods_policy_version DEFAULT (1) WITH VALUES;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_project_periods_policy_version'
                   AND parent_object_id = OBJECT_ID(N'dbo.project_periods'))
        EXEC(N'ALTER TABLE dbo.project_periods ADD CONSTRAINT ck_project_periods_policy_version CHECK (policy_version >= 1)');

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
