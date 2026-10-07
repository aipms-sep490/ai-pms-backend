SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID('dbo.chat_conversations','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_conversations (
        id bigint IDENTITY PRIMARY KEY,
        kind nvarchar(10) NOT NULL,
        team_id bigint NULL REFERENCES dbo.teams(id),
        project_id bigint NULL REFERENCES dbo.projects(id),
        first_user_id bigint NULL REFERENCES dbo.users(id),
        second_user_id bigint NULL REFERENCES dbo.users(id),
        status nvarchar(12) NOT NULL DEFAULT 'OPEN',
        sequence bigint NOT NULL DEFAULT 0,
        version bigint NOT NULL DEFAULT 0,
        created_at datetime2(7) NOT NULL,
        updated_at datetime2(7) NOT NULL,
        concurrency_token uniqueidentifier NOT NULL DEFAULT NEWID(),
        CONSTRAINT ck_chat_conversation_shape CHECK (
            (kind='DIRECT' AND team_id IS NULL AND project_id IS NULL AND first_user_id IS NOT NULL AND second_user_id IS NOT NULL AND first_user_id<second_user_id) OR
            (kind='TEAM' AND team_id IS NOT NULL AND project_id IS NULL AND first_user_id IS NULL AND second_user_id IS NULL) OR
            (kind='PROJECT' AND project_id IS NOT NULL AND team_id IS NULL AND first_user_id IS NULL AND second_user_id IS NULL)),
        CONSTRAINT ck_chat_conversation_state CHECK(status IN ('OPEN','READ_ONLY') AND sequence>=0 AND version>=0)
    );
    CREATE UNIQUE INDEX ux_chat_direct ON dbo.chat_conversations(first_user_id,second_user_id) WHERE kind='DIRECT';
    CREATE UNIQUE INDEX ux_chat_team ON dbo.chat_conversations(team_id) WHERE kind='TEAM';
    CREATE UNIQUE INDEX ux_chat_project ON dbo.chat_conversations(project_id) WHERE kind='PROJECT';
    CREATE INDEX ix_chat_inbox ON dbo.chat_conversations(updated_at DESC,id DESC);
END;
IF OBJECT_ID('dbo.chat_membership_intervals','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_membership_intervals (
        id bigint IDENTITY PRIMARY KEY,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        user_id bigint NOT NULL REFERENCES dbo.users(id),
        from_sequence bigint NOT NULL,
        left_sequence bigint NULL,
        source_key nvarchar(64) NOT NULL,
        retained bit NOT NULL DEFAULT 0,
        joined_at datetime2(7) NOT NULL,
        left_at datetime2(7) NULL,
        CONSTRAINT ck_chat_interval CHECK(from_sequence>0 AND (left_sequence IS NULL OR left_sequence>=from_sequence-1))
    );
    CREATE UNIQUE INDEX ux_chat_open_member ON dbo.chat_membership_intervals(conversation_id,user_id) WHERE left_at IS NULL;
    CREATE INDEX ix_chat_member_lookup ON dbo.chat_membership_intervals(user_id,conversation_id,left_at) INCLUDE(from_sequence,retained);
END;
IF OBJECT_ID('dbo.chat_messages','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_messages (
        id bigint IDENTITY PRIMARY KEY,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        sequence bigint NOT NULL,
        sender_id bigint NOT NULL REFERENCES dbo.users(id),
        client_message_id uniqueidentifier NOT NULL,
        request_hash varchar(64) NOT NULL,
        reply_to_message_id bigint NULL,
        body nvarchar(4000) NULL,
        created_at datetime2(7) NOT NULL,
        edited_at datetime2(7) NULL,
        recalled_at datetime2(7) NULL,
        concurrency_token uniqueidentifier NOT NULL,
        CONSTRAINT uq_chat_message_parent UNIQUE(conversation_id,id),
        CONSTRAINT fk_chat_reply FOREIGN KEY(conversation_id,reply_to_message_id) REFERENCES dbo.chat_messages(conversation_id,id),
        CONSTRAINT ck_chat_message_body CHECK((recalled_at IS NULL AND body IS NOT NULL AND LEN(body)>0) OR (recalled_at IS NOT NULL AND body IS NULL)),
        CONSTRAINT ck_chat_message_sequence CHECK(sequence>0)
    );
    CREATE UNIQUE INDEX ux_chat_message_order ON dbo.chat_messages(conversation_id,sequence);
    CREATE UNIQUE INDEX ux_chat_message_retry ON dbo.chat_messages(conversation_id,sender_id,client_message_id);
END;
IF OBJECT_ID('dbo.chat_member_state','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_member_state (
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        user_id bigint NOT NULL REFERENCES dbo.users(id),
        last_read_sequence bigint NOT NULL DEFAULT 0 CHECK(last_read_sequence>=0),
        updated_at datetime2(7) NOT NULL,
        PRIMARY KEY(conversation_id,user_id)
    );
END;
IF OBJECT_ID('dbo.chat_outbox','U') IS NULL
BEGIN
    CREATE TABLE dbo.chat_outbox (
        id bigint IDENTITY PRIMARY KEY,
        event_id uniqueidentifier NOT NULL UNIQUE,
        conversation_id bigint NOT NULL REFERENCES dbo.chat_conversations(id),
        version bigint NOT NULL,
        event_type nvarchar(32) NOT NULL,
        status nvarchar(12) NOT NULL DEFAULT 'PENDING',
        attempt_count int NOT NULL DEFAULT 0,
        next_attempt_at datetime2(7) NOT NULL,
        lease_token uniqueidentifier NULL,
        lease_until datetime2(7) NULL,
        created_at datetime2(7) NOT NULL,
        completed_at datetime2(7) NULL,
        error_code nvarchar(64) NULL,
        CONSTRAINT ck_chat_outbox_status CHECK(status IN ('PENDING','PROCESSING','SUCCEEDED','FAILED')),
        CONSTRAINT ck_chat_outbox_attempt CHECK(attempt_count>=0)
    );
    CREATE INDEX ix_chat_outbox_claim ON dbo.chat_outbox(status,next_attempt_at,lease_until);
END;
COMMIT;
GO
-- This view grants chat participation, never platform-wide Admin access.
CREATE OR ALTER VIEW dbo.chat_scope_members AS
SELECT CAST('TEAM' AS nvarchar(10)) AS kind, tm.team_id AS scope_id, tm.user_id,
    CAST(CONCAT('T:',tm.id,':',CONVERT(varchar(33),tm.joined_at,126)) AS nvarchar(128)) AS source_key
FROM dbo.team_members tm JOIN dbo.users u ON u.id=tm.user_id
JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE tm.left_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='STUDENT')
UNION ALL
SELECT 'PROJECT',p.id,tm.user_id,CAST(CONCAT('T:',tm.id,':',CONVERT(varchar(33),tm.joined_at,126)) AS nvarchar(128))
FROM dbo.projects p JOIN dbo.team_members tm ON tm.team_id=p.team_id JOIN dbo.users u ON u.id=tm.user_id
JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE tm.left_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='STUDENT')
UNION ALL
SELECT 'PROJECT',a.project_id,sp.user_id,CAST(CONCAT('S:',a.id,':',CONVERT(varchar(33),a.assigned_at,126)) AS nvarchar(128))
FROM dbo.supervisor_assignments a JOIN dbo.supervisor_profiles sp ON sp.id=a.supervisor_profile_id
JOIN dbo.users u ON u.id=sp.user_id JOIN dbo.departments d ON d.id=u.department_id JOIN dbo.organizations o ON o.id=d.organization_id
WHERE a.ended_at IS NULL AND u.status='ACTIVE' AND d.is_active=1 AND o.is_active=1 AND a.assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR')
AND EXISTS(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id=ur.role_id WHERE ur.user_id=u.id AND r.code='LECTURER');
GO
-- Close an interval at the source mutation, even if no chat request observes the gap.
CREATE OR ALTER TRIGGER dbo.tr_chat_team_members ON dbo.team_members AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND cm.retained=0 AND EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.id=d.id
        WHERE d.user_id=cm.user_id AND (i.id IS NULL OR i.left_at IS NOT NULL OR i.joined_at<>d.joined_at OR i.user_id<>d.user_id OR i.team_id<>d.team_id)
        AND (c.kind='DIRECT' OR c.team_id=d.team_id OR EXISTS(SELECT 1 FROM dbo.projects p WHERE p.id=c.project_id AND p.team_id=d.team_id)));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_supervisors ON dbo.supervisor_assignments AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND cm.retained=0 AND EXISTS (
        SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.id=d.id JOIN dbo.supervisor_profiles sp ON sp.id=d.supervisor_profile_id
        WHERE sp.user_id=cm.user_id AND (i.id IS NULL OR i.ended_at IS NOT NULL OR i.supervisor_profile_id<>d.supervisor_profile_id OR i.project_id<>d.project_id)
        AND (c.kind='DIRECT' OR c.project_id=d.project_id));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_users ON dbo.users AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT (UPDATE(status) OR UPDATE(department_id)) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    JOIN inserted i ON i.id=cm.user_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND (i.status<>d.status OR ISNULL(i.department_id,0)<>ISNULL(d.department_id,0));
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_roles ON dbo.user_roles AFTER DELETE,UPDATE AS
BEGIN
    SET NOCOUNT ON;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id
    WHERE cm.left_at IS NULL AND EXISTS(SELECT 1 FROM deleted d WHERE d.user_id=cm.user_id);
END;
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
CREATE OR ALTER TRIGGER dbo.tr_chat_team_close ON dbo.teams AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(status) RETURN;
    UPDATE cm SET retained=1 FROM dbo.chat_membership_intervals cm
    JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN inserted i ON i.id=c.team_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND c.kind='TEAM' AND c.status='OPEN'
        AND i.status IN ('DISBANDED','CLOSED','ARCHIVED') AND i.status<>d.status
        AND EXISTS(SELECT 1 FROM dbo.chat_scope_members s WHERE s.kind='TEAM' AND s.scope_id=i.id AND s.user_id=cm.user_id);
    UPDATE c SET status='READ_ONLY',updated_at=SYSUTCDATETIME(),version=version+1
    FROM dbo.chat_conversations c JOIN inserted i ON i.id=c.team_id
    WHERE c.kind='TEAM' AND c.status='OPEN' AND i.status IN ('DISBANDED','CLOSED','ARCHIVED');
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_department ON dbo.departments AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(is_active) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN dbo.users u ON u.id=cm.user_id
    JOIN inserted i ON i.id=u.department_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND i.is_active=0 AND d.is_active=1;
END;
GO
CREATE OR ALTER TRIGGER dbo.tr_chat_organization ON dbo.organizations AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(is_active) RETURN;
    UPDATE cm SET left_at=SYSUTCDATETIME(),left_sequence=c.sequence
    FROM dbo.chat_membership_intervals cm JOIN dbo.chat_conversations c ON c.id=cm.conversation_id JOIN dbo.users u ON u.id=cm.user_id
    JOIN dbo.departments dept ON dept.id=u.department_id JOIN inserted i ON i.id=dept.organization_id JOIN deleted d ON d.id=i.id
    WHERE cm.left_at IS NULL AND i.is_active=0 AND d.is_active=1;
END;
GO
