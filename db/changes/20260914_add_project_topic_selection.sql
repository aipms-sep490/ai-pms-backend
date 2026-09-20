-- Additive migration for project topic selection and proposal source.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF COL_LENGTH('dbo.projects', 'topic_id') IS NULL
    BEGIN
        ALTER TABLE dbo.projects ADD topic_id BIGINT NULL;
    END;

    IF COL_LENGTH('dbo.projects', 'proposal_source') IS NULL
    BEGIN
        ALTER TABLE dbo.projects ADD proposal_source VARCHAR(30) NOT NULL CONSTRAINT df_projects_proposal_source DEFAULT ('STUDENT_PROPOSAL');
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_projects_proposal_source' AND parent_object_id = OBJECT_ID('dbo.projects'))
    BEGIN
        EXEC(N'ALTER TABLE dbo.projects ADD CONSTRAINT ck_projects_proposal_source CHECK (proposal_source IN (''STUDENT_PROPOSAL'', ''PUBLISHED_TOPIC''));');
    END;

    IF OBJECT_ID('dbo.project_topics') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_projects_topic' AND parent_object_id = OBJECT_ID('dbo.projects'))
    BEGIN
        EXEC(N'ALTER TABLE dbo.projects ADD CONSTRAINT fk_projects_topic FOREIGN KEY (topic_id) REFERENCES dbo.project_topics(id) ON DELETE NO ACTION ON UPDATE NO ACTION;');
    END;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_projects_topic_id' AND object_id = OBJECT_ID('dbo.projects'))
    BEGIN
        EXEC(N'CREATE NONCLUSTERED INDEX ix_projects_topic_id ON dbo.projects(topic_id) WHERE topic_id IS NOT NULL;');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
