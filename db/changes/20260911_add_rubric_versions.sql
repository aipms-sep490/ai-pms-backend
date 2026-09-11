-- Apply after db/schema.sql on a new database, or to an existing AI-PMS database.
-- Review and apply before deploying the Rubrics API; does not switch databases.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.rubric_versions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.rubric_versions (
            rubric_id BIGINT NOT NULL,
            root_rubric_id BIGINT NOT NULL,
            version_number INT NOT NULL,
            status VARCHAR(20) NOT NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL,
            CONSTRAINT pk_rubric_versions PRIMARY KEY (rubric_id),
            CONSTRAINT fk_rubric_versions_rubric FOREIGN KEY (rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT fk_rubric_versions_root FOREIGN KEY (root_rubric_id) REFERENCES dbo.rubrics(id),
            CONSTRAINT uq_rubric_versions_family_number UNIQUE (root_rubric_id, version_number),
            CONSTRAINT ck_rubric_versions_status CHECK (status IN ('DRAFT','PUBLISHED','RETIRED')),
            CONSTRAINT ck_rubric_versions_number CHECK (version_number > 0)
        );
    END;

    -- No reliable publication history exists for legacy rows. Protect all of them;
    -- never infer an editable draft from is_active = 0 or rename/relink evaluations.
    INSERT INTO dbo.rubric_versions (rubric_id, root_rubric_id, version_number, status, concurrency_token)
    SELECT r.id, r.id, 1, CASE WHEN r.is_active = 1 THEN 'PUBLISHED' ELSE 'RETIRED' END, NEWID()
    FROM dbo.rubrics r WITH (UPDLOCK, HOLDLOCK)
    WHERE NOT EXISTS (SELECT 1 FROM dbo.rubric_versions v WITH (UPDLOCK, HOLDLOCK) WHERE v.rubric_id = r.id);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
