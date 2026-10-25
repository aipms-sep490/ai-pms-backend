SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.assistant_conversations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.assistant_conversations (
        id bigint IDENTITY CONSTRAINT pk_assistant_conversations PRIMARY KEY,
        actor_id bigint NOT NULL CONSTRAINT fk_assistant_conversation_actor REFERENCES dbo.users(id),
        project_id bigint NULL CONSTRAINT fk_assistant_conversation_project REFERENCES dbo.projects(id),
        department_id bigint NULL CONSTRAINT fk_assistant_conversation_department REFERENCES dbo.departments(id),
        scope varchar(20) NOT NULL CONSTRAINT ck_assistant_conversation_scope CHECK(scope IN ('ACADEMIC','PROJECT','COMBINED')),
        title nvarchar(120) NOT NULL,
        revision int NOT NULL CONSTRAINT ck_assistant_conversation_revision CHECK(revision >= 0),
        updated_at datetime2(7) NOT NULL,
        deleted_at datetime2(7) NULL
    );
    CREATE INDEX ix_assistant_conversations_owner ON dbo.assistant_conversations(actor_id, project_id, id);
END;
IF OBJECT_ID('dbo.assistant_conversation_turns', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.assistant_conversation_turns (
        id bigint IDENTITY CONSTRAINT pk_assistant_conversation_turns PRIMARY KEY,
        conversation_id bigint NOT NULL CONSTRAINT fk_assistant_turn_conversation REFERENCES dbo.assistant_conversations(id),
        revision int NOT NULL CONSTRAINT ck_assistant_turn_revision CHECK(revision > 0),
        query nvarchar(2000) NOT NULL,
        run_id bigint NOT NULL CONSTRAINT fk_assistant_turn_run REFERENCES dbo.academic_knowledge_query_runs(id),
        CONSTRAINT uq_assistant_turn_revision UNIQUE(conversation_id, revision)
    );
END;
COMMIT;
