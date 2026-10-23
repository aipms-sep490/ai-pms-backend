SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID('dbo.academic_knowledge_documents','U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_knowledge_documents (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_knowledge_documents PRIMARY KEY,
        title nvarchar(255) NOT NULL,
        document_type varchar(50) NOT NULL,
        department_id bigint NULL,
        academic_semester_id bigint NULL,
        project_period_id bigint NULL,
        status varchar(20) NOT NULL CONSTRAINT df_knowledge_document_status DEFAULT('DRAFT'),
        current_version nvarchar(64) NOT NULL CONSTRAINT df_knowledge_document_current_version DEFAULT(N'1.0'),
        created_by bigint NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_knowledge_document_created_at DEFAULT(sysutcdatetime()),
        updated_at datetime2(7) NOT NULL CONSTRAINT df_knowledge_document_updated_at DEFAULT(sysutcdatetime()),
        CONSTRAINT ck_knowledge_document_status CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
        CONSTRAINT fk_knowledge_document_department FOREIGN KEY(department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_knowledge_document_semester FOREIGN KEY(academic_semester_id) REFERENCES dbo.academic_semesters(id),
        CONSTRAINT fk_knowledge_document_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id),
        CONSTRAINT fk_knowledge_document_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id)
    );
    CREATE INDEX ix_knowledge_documents_scope ON dbo.academic_knowledge_documents(status, department_id, academic_semester_id, project_period_id);
END;

IF OBJECT_ID('dbo.academic_knowledge_document_versions','U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_knowledge_document_versions (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_knowledge_document_versions PRIMARY KEY,
        document_id bigint NOT NULL,
        version nvarchar(64) NOT NULL,
        status varchar(20) NOT NULL CONSTRAINT df_knowledge_document_version_status DEFAULT('DRAFT'),
        content nvarchar(max) NOT NULL,
        source_file_name nvarchar(255) NULL,
        source_checksum varchar(128) NULL,
        effective_from datetime2(7) NULL,
        effective_to datetime2(7) NULL,
        created_by bigint NOT NULL,
        published_by bigint NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_knowledge_document_version_created_at DEFAULT(sysutcdatetime()),
        published_at datetime2(7) NULL,
        CONSTRAINT uq_knowledge_document_version UNIQUE(document_id, version),
        CONSTRAINT ck_knowledge_document_version_status CHECK(status IN ('DRAFT','PUBLISHED','RETIRED')),
        CONSTRAINT fk_knowledge_document_version_document FOREIGN KEY(document_id) REFERENCES dbo.academic_knowledge_documents(id) ON DELETE CASCADE,
        CONSTRAINT fk_knowledge_document_version_creator FOREIGN KEY(created_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_knowledge_document_version_publisher FOREIGN KEY(published_by) REFERENCES dbo.users(id)
    );
END;

IF OBJECT_ID('dbo.academic_knowledge_chunks','U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_knowledge_chunks (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_knowledge_chunks PRIMARY KEY,
        document_version_id bigint NOT NULL,
        ordinal int NOT NULL,
        heading nvarchar(500) NULL,
        page_number int NULL,
        content nvarchar(max) NOT NULL,
        search_text nvarchar(max) NOT NULL,
        CONSTRAINT uq_knowledge_chunk_ordinal UNIQUE(document_version_id, ordinal),
        CONSTRAINT fk_knowledge_chunk_version FOREIGN KEY(document_version_id) REFERENCES dbo.academic_knowledge_document_versions(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_knowledge_chunks_search ON dbo.academic_knowledge_chunks(document_version_id, ordinal);
END;

IF OBJECT_ID('dbo.academic_knowledge_index_jobs','U') IS NULL
BEGIN
    CREATE TABLE dbo.academic_knowledge_index_jobs (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_knowledge_index_jobs PRIMARY KEY,
        document_version_id bigint NOT NULL,
        status varchar(20) NOT NULL CONSTRAINT df_knowledge_index_status DEFAULT('PENDING'),
        error nvarchar(2000) NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_knowledge_index_created_at DEFAULT(sysutcdatetime()),
        completed_at datetime2(7) NULL,
        CONSTRAINT ck_knowledge_index_status CHECK(status IN ('PENDING','RUNNING','COMPLETED','FAILED')),
        CONSTRAINT fk_knowledge_index_version FOREIGN KEY(document_version_id) REFERENCES dbo.academic_knowledge_document_versions(id) ON DELETE CASCADE
    );
END;
IF OBJECT_ID('dbo.academic_knowledge_query_runs','U') IS NULL
BEGIN
 CREATE TABLE dbo.academic_knowledge_query_runs(id bigint IDENTITY PRIMARY KEY,actor_id bigint NOT NULL,project_id bigint NULL,scope varchar(20) NOT NULL,query_hash varchar(64) NOT NULL,status varchar(24) NOT NULL,model_version nvarchar(128) NOT NULL,evidence_json nvarchar(max) NOT NULL,result_json nvarchar(max) NOT NULL,started_at datetime2(7) NOT NULL,completed_at datetime2(7) NOT NULL,CONSTRAINT fk_knowledge_query_actor FOREIGN KEY(actor_id) REFERENCES dbo.users(id),CONSTRAINT fk_knowledge_query_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id));
 CREATE INDEX ix_knowledge_query_actor ON dbo.academic_knowledge_query_runs(actor_id,id DESC);
END;

COMMIT;
GO
