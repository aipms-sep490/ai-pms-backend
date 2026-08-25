-- AI-PMS Migration Script
-- Date: 2026-08-25
-- Description: Add structured project metadata fields, rowversion concurrency, and keywords table.

USE [AI_PMS]; -- Default database
GO

PRINT '1. Adding metadata and concurrency columns to dbo.projects...';
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.projects') AND name = N'problem_statement')
BEGIN
    ALTER TABLE dbo.projects ADD problem_statement NVARCHAR(MAX) NULL;
    ALTER TABLE dbo.projects ADD domain NVARCHAR(250) NULL;
    ALTER TABLE dbo.projects ADD technology NVARCHAR(250) NULL;
    ALTER TABLE dbo.projects ADD expected_output NVARCHAR(1000) NULL;
    ALTER TABLE dbo.projects ADD row_version ROWVERSION NOT NULL;
    PRINT 'Successfully added columns to dbo.projects.';
END
ELSE
BEGIN
    PRINT 'Columns already exist on dbo.projects.';
END
GO

PRINT '2. Creating dbo.project_keywords table...';
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'dbo.project_keywords') AND type in (N'U'))
BEGIN
    CREATE TABLE dbo.project_keywords (
        id              BIGINT IDENTITY(1,1) NOT NULL,
        project_id      BIGINT NOT NULL,
        keyword         NVARCHAR(100) NOT NULL,
        CONSTRAINT pk_project_keywords PRIMARY KEY CLUSTERED (id ASC),
        CONSTRAINT fk_project_keywords_project FOREIGN KEY (project_id)
            REFERENCES dbo.projects (id) ON DELETE CASCADE
    );

    CREATE NONCLUSTERED INDEX ix_project_keywords_project ON dbo.project_keywords (project_id ASC);
    CREATE NONCLUSTERED INDEX ix_project_keywords_keyword ON dbo.project_keywords (keyword ASC);
    PRINT 'Successfully created dbo.project_keywords table and its indexes.';
END
ELSE
BEGIN
    PRINT 'Table dbo.project_keywords already exists.';
END
GO

PRINT 'Migration complete.';
GO
