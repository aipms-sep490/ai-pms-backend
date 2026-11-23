SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.assistant_conversations', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.assistant_conversations', N'is_pinned') IS NULL
    BEGIN
        ALTER TABLE dbo.assistant_conversations
            ADD is_pinned bit NOT NULL
                CONSTRAINT df_assistant_conversations_is_pinned DEFAULT (0);
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.assistant_conversations')
          AND name = N'ix_assistant_conversations_actor_pin'
    )
    BEGIN
        EXEC(N'CREATE INDEX ix_assistant_conversations_actor_pin
            ON dbo.assistant_conversations(actor_id, project_id, is_pinned DESC, updated_at DESC, id DESC)
            WHERE deleted_at IS NULL;');
    END;
END;

IF OBJECT_ID(N'dbo.academic_knowledge_query_runs', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.academic_knowledge_query_runs', N'feedback_rating') IS NULL
    BEGIN
        ALTER TABLE dbo.academic_knowledge_query_runs
            ADD feedback_rating smallint NULL;
    END;

    IF COL_LENGTH(N'dbo.academic_knowledge_query_runs', N'feedback_note') IS NULL
    BEGIN
        ALTER TABLE dbo.academic_knowledge_query_runs
            ADD feedback_note nvarchar(400) NULL;
    END;

    IF COL_LENGTH(N'dbo.academic_knowledge_query_runs', N'feedback_at') IS NULL
    BEGIN
        ALTER TABLE dbo.academic_knowledge_query_runs
            ADD feedback_at datetime2(7) NULL;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.academic_knowledge_query_runs')
          AND name = N'ck_academic_knowledge_query_runs_feedback'
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.academic_knowledge_query_runs
            ADD CONSTRAINT ck_academic_knowledge_query_runs_feedback
                CHECK (feedback_rating IS NULL OR feedback_rating IN (-1, 1));');
    END;
END;

COMMIT TRANSACTION;
