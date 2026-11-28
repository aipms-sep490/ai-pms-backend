-- Rollback for 20261216_chat_direct_archive_readonly.sql
SET XACT_ABORT ON;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_project_close ON dbo.projects AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;
    UPDATE cm SET retained=1 FROM dbo.chat_membership_intervals cm
    JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN inserted i ON i.id=c.project_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND c.kind='PROJECT' AND c.status='OPEN'
        AND i.status IN ('COMPLETED','ARCHIVED','CANCELLED') AND i.status<>d.status
        AND EXISTS(SELECT 1 FROM dbo.chat_scope_members s WHERE s.kind='PROJECT' AND s.scope_id=i.id AND s.user_id=cm.user_id);
    UPDATE c SET status='READ_ONLY',updated_at=SYSUTCDATETIME(),version=version+1
    FROM dbo.chat_conversations c JOIN inserted i ON i.id=c.project_id
    WHERE c.kind='PROJECT' AND c.status='OPEN' AND i.status IN ('COMPLETED','ARCHIVED','CANCELLED');
END;
GO
