-- Existing registration snapshot JSON remains immutable. New submissions capture
-- proposal and project requirement values (including IDs/tokens) in that envelope.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.project_major_requirements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_major_requirements (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_major_requirements PRIMARY KEY,
        project_id BIGINT NOT NULL,
        major_id BIGINT NOT NULL,
        min_members INT NOT NULL,
        max_members INT NOT NULL,
        responsibility NVARCHAR(2000) NOT NULL,
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_project_major_requirements_token DEFAULT NEWID(),
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_project_major_requirements_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(7) NOT NULL CONSTRAINT df_project_major_requirements_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_project_major_requirements_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_major_requirements_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT uq_project_major_requirements UNIQUE(project_id, major_id),
        CONSTRAINT ck_project_major_requirements_bounds CHECK(min_members >= 1 AND max_members >= min_members),
        CONSTRAINT ck_project_major_requirements_responsibility CHECK(LEN(LTRIM(RTRIM(responsibility))) > 0)
    );
    CREATE INDEX ix_project_major_requirements_major ON dbo.project_major_requirements(major_id);
END;
-- No speculative backfill from team quotas or current proposals into old snapshots.
COMMIT;
