-- Additive catalogue foundation. Run against the intended AI-PMS database.
-- Does not create projects, reserve topics, or infer historical proposal sources.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.project_topics', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_topics (
            id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_topics PRIMARY KEY,
            project_period_id bigint NOT NULL,
            lead_department_id bigint NOT NULL,
            code varchar(50) NOT NULL,
            status varchar(20) NOT NULL,
            title nvarchar(300) NOT NULL,
            description nvarchar(4000) NULL,
            problem_statement nvarchar(4000) NULL,
            objectives nvarchar(4000) NULL,
            expected_output nvarchar(4000) NULL,
            domain nvarchar(200) NULL,
            technologies_json nvarchar(max) NOT NULL,
            keywords_json nvarchar(max) NOT NULL,
            project_mode varchar(30) NOT NULL,
            primary_major_id bigint NULL,
            created_by bigint NOT NULL,
            updated_by bigint NOT NULL,
            created_at datetime2(0) NOT NULL,
            updated_at datetime2(0) NOT NULL,
            published_by bigint NULL,
            published_at datetime2(0) NULL,
            closed_by bigint NULL,
            closed_at datetime2(0) NULL,
            close_reason nvarchar(2000) NULL,
            concurrency_token uniqueidentifier NOT NULL,
            CONSTRAINT fk_topic_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id),
            CONSTRAINT fk_topic_lead FOREIGN KEY(lead_department_id) REFERENCES dbo.departments(id),
            CONSTRAINT fk_topic_primary_major FOREIGN KEY(primary_major_id) REFERENCES dbo.majors(id),
            CONSTRAINT fk_topic_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_topic_updater FOREIGN KEY(updated_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_topic_publisher FOREIGN KEY(published_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_topic_closer FOREIGN KEY(closed_by) REFERENCES dbo.users(id),
            CONSTRAINT ck_topic_identity CHECK (LEN(LTRIM(RTRIM(code))) > 0 AND LEN(LTRIM(RTRIM(title))) > 0),
            CONSTRAINT ck_topic_mode CHECK (
                (project_mode = 'SINGLE_MAJOR' AND primary_major_id IS NOT NULL)
                OR (project_mode = 'INTERDISCIPLINARY' AND primary_major_id IS NULL)),
            CONSTRAINT ck_topic_tags CHECK (ISJSON(technologies_json) = 1 AND ISJSON(keywords_json) = 1
                AND LEFT(LTRIM(technologies_json), 1) = '[' AND LEFT(LTRIM(keywords_json), 1) = '['),
            CONSTRAINT ck_topic_lifecycle CHECK (
                (status = 'DRAFT' AND published_by IS NULL AND published_at IS NULL AND closed_by IS NULL AND closed_at IS NULL AND close_reason IS NULL)
                OR (status = 'PUBLISHED' AND published_by IS NOT NULL AND published_at IS NOT NULL AND closed_by IS NULL AND closed_at IS NULL AND close_reason IS NULL)
                OR (status = 'CLOSED' AND closed_by IS NOT NULL AND closed_at IS NOT NULL AND close_reason IS NOT NULL AND LEN(LTRIM(RTRIM(close_reason))) > 0
                    AND ((published_by IS NULL AND published_at IS NULL) OR (published_by IS NOT NULL AND published_at IS NOT NULL))))
        );
        CREATE UNIQUE INDEX uq_project_topics_period_code ON dbo.project_topics(project_period_id, code);
        CREATE INDEX ix_project_topics_period_status ON dbo.project_topics(project_period_id, status, id);
    END;
    IF OBJECT_ID(N'dbo.topic_major_requirements', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.topic_major_requirements (
            topic_id bigint NOT NULL,
            major_id bigint NOT NULL,
            department_id bigint NOT NULL,
            min_members int NOT NULL,
            max_members int NOT NULL,
            responsibility nvarchar(1000) NOT NULL,
            CONSTRAINT pk_topic_major_requirements PRIMARY KEY(topic_id, major_id),
            CONSTRAINT fk_topic_requirement_topic FOREIGN KEY(topic_id) REFERENCES dbo.project_topics(id),
            CONSTRAINT fk_topic_requirement_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
            CONSTRAINT fk_topic_requirement_department FOREIGN KEY(department_id) REFERENCES dbo.departments(id),
            CONSTRAINT ck_topic_requirement_quota CHECK (min_members >= 1 AND max_members >= min_members AND max_members <= 100),
            CONSTRAINT ck_topic_requirement_responsibility CHECK (LEN(LTRIM(RTRIM(responsibility))) > 0)
        );
        CREATE INDEX ix_topic_requirement_major ON dbo.topic_major_requirements(major_id, topic_id);
        CREATE INDEX ix_topic_requirement_department ON dbo.topic_major_requirements(department_id, topic_id);
    END;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
