-- TK-W1-16: Add max_teams (default 1, >= 1), track_code, and suggested_supervisor_profile_id to dbo.project_topics
SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.project_topics', N'max_teams') IS NULL
BEGIN
    ALTER TABLE dbo.project_topics
        ADD max_teams INT NOT NULL CONSTRAINT df_project_topics_max_teams DEFAULT (1);
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'ck_project_topics_max_teams'
      AND parent_object_id = OBJECT_ID(N'dbo.project_topics')
)
BEGIN
    ALTER TABLE dbo.project_topics
        ADD CONSTRAINT ck_project_topics_max_teams CHECK (max_teams >= 1);
END;

IF COL_LENGTH(N'dbo.project_topics', N'track_code') IS NULL
BEGIN
    ALTER TABLE dbo.project_topics
        ADD track_code VARCHAR(20) NULL;
END;

IF COL_LENGTH(N'dbo.project_topics', N'suggested_supervisor_profile_id') IS NULL
BEGIN
    ALTER TABLE dbo.project_topics
        ADD suggested_supervisor_profile_id BIGINT NULL;
END;
GO
