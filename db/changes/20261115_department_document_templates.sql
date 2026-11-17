SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.department_document_templates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.department_document_templates (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_department_document_templates PRIMARY KEY,
        department_id bigint NOT NULL CONSTRAINT fk_department_document_templates_department REFERENCES dbo.departments(id),
        major_id bigint NOT NULL CONSTRAINT fk_department_document_templates_major REFERENCES dbo.majors(id),
        project_period_id bigint NULL CONSTRAINT fk_department_document_templates_period REFERENCES dbo.project_periods(id),
        code varchar(60) NOT NULL,
        title nvarchar(200) NOT NULL,
        version int NOT NULL CONSTRAINT ck_department_document_templates_version CHECK(version > 0),
        status varchar(20) NOT NULL CONSTRAINT ck_department_document_templates_status CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
        file_name nvarchar(255) NOT NULL,
        content_type varchar(150) NOT NULL,
        size_bytes bigint NOT NULL CONSTRAINT ck_department_document_templates_size CHECK(size_bytes > 0 AND size_bytes <= 20971520),
        sha256 char(64) NOT NULL,
        storage_key varchar(80) NOT NULL,
        concurrency_token uniqueidentifier NOT NULL,
        uploaded_by bigint NOT NULL CONSTRAINT fk_department_document_templates_uploader REFERENCES dbo.users(id),
        uploaded_at datetime2(0) NOT NULL,
        published_at datetime2(0) NULL
    );
    CREATE UNIQUE INDEX ux_department_document_templates_version
        ON dbo.department_document_templates(department_id, major_id, project_period_id, code, version);
    CREATE INDEX ix_department_document_templates_scope
        ON dbo.department_document_templates(major_id, project_period_id, status, code, version DESC);
END;
COMMIT;
