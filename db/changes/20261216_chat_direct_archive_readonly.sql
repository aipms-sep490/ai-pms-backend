-- Wave 4 (TK-W4-06): Transition project-related DIRECT conversations to READ_ONLY + retained=1 on ARCHIVED
-- when the two users no longer share any other live TEAM or PROJECT scope.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
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

    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.id=i.id WHERE i.status='ARCHIVED' AND d.status<>'ARCHIVED')
    BEGIN
        DECLARE @archivedDirect TABLE (conversation_id BIGINT PRIMARY KEY);

        INSERT INTO @archivedDirect (conversation_id)
        SELECT DISTINCT c.id
        FROM dbo.chat_conversations c
        JOIN inserted i ON i.status='ARCHIVED'
        JOIN deleted d ON d.id=i.id AND d.status<>'ARCHIVED'
        WHERE c.kind='DIRECT' AND c.status='OPEN'
          AND (
              EXISTS (SELECT 1 FROM dbo.team_members tm WHERE tm.team_id=i.team_id AND tm.user_id=c.first_user_id)
              OR EXISTS (SELECT 1 FROM dbo.supervisor_assignments sa JOIN dbo.supervisor_profiles sp ON sp.id=sa.supervisor_profile_id WHERE sa.project_id=i.id AND sp.user_id=c.first_user_id)
          )
          AND (
              EXISTS (SELECT 1 FROM dbo.team_members tm WHERE tm.team_id=i.team_id AND tm.user_id=c.second_user_id)
              OR EXISTS (SELECT 1 FROM dbo.supervisor_assignments sa JOIN dbo.supervisor_profiles sp ON sp.id=sa.supervisor_profile_id WHERE sa.project_id=i.id AND sp.user_id=c.second_user_id)
          )
          AND NOT EXISTS (
              SELECT 1
              FROM dbo.chat_scope_members s1
              JOIN dbo.chat_scope_members s2 ON s2.kind=s1.kind AND s2.scope_id=s1.scope_id
              WHERE s1.user_id=c.first_user_id AND s2.user_id=c.second_user_id
                AND (
                    (s1.kind='PROJECT' AND EXISTS (
                        SELECT 1 FROM dbo.projects p
                        WHERE p.id=s1.scope_id AND p.status NOT IN ('COMPLETED','ARCHIVED','CANCELLED')
                    ))
                    OR
                    (s1.kind='TEAM' AND s1.scope_id<>i.team_id AND EXISTS (
                        SELECT 1 FROM dbo.teams t
                        WHERE t.id=s1.scope_id AND t.status NOT IN ('DISBANDED','CLOSED','ARCHIVED')
                          AND NOT EXISTS (SELECT 1 FROM dbo.projects tp WHERE tp.team_id=t.id AND tp.status IN ('ARCHIVED','CANCELLED'))
                    ))
                )
          );

        IF EXISTS (SELECT 1 FROM @archivedDirect)
        BEGIN
            UPDATE cm SET retained=1
            FROM dbo.chat_membership_intervals cm
            JOIN @archivedDirect ad ON ad.conversation_id=cm.conversation_id
            WHERE cm.left_at IS NULL;

            UPDATE c SET status='READ_ONLY', updated_at=SYSUTCDATETIME(), version=version+1
            FROM dbo.chat_conversations c
            JOIN @archivedDirect ad ON ad.conversation_id=c.id
            WHERE c.status='OPEN';
        END;
    END;
END;
GO
