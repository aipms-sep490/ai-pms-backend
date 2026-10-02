SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.meetings', N'meeting_delivery_mode') IS NULL
    ALTER TABLE dbo.meetings ADD meeting_delivery_mode VARCHAR(20) NULL;
IF COL_LENGTH(N'dbo.meetings', N'video_channel') IS NULL
    ALTER TABLE dbo.meetings ADD video_channel VARCHAR(30) NULL;

UPDATE dbo.meetings
SET meeting_delivery_mode = CASE WHEN location IS NOT NULL THEN CASE WHEN online_url IS NULL THEN 'ONSITE' ELSE 'HYBRID' END ELSE CASE WHEN online_url IS NULL THEN 'ONSITE' ELSE 'REMOTE' END END,
    video_channel = CASE WHEN online_url IS NULL THEN 'NONE' ELSE 'EXTERNAL_LINK' END
WHERE meeting_delivery_mode IS NULL OR video_channel IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_meetings_delivery_mode')
    ALTER TABLE dbo.meetings ADD CONSTRAINT ck_meetings_delivery_mode CHECK (meeting_delivery_mode IN ('ONSITE','REMOTE','HYBRID'));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_meetings_video_channel')
    ALTER TABLE dbo.meetings ADD CONSTRAINT ck_meetings_video_channel CHECK (video_channel IN ('NONE','EXTERNAL_LINK','IN_APP_VIDEO'));
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.meetings') AND name = N'ix_meetings_video_channel')
    CREATE INDEX ix_meetings_video_channel ON dbo.meetings(video_channel, status);

IF OBJECT_ID(N'dbo.meeting_video_sessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_video_sessions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_sessions PRIMARY KEY,
        meeting_id BIGINT NOT NULL,
        provider VARCHAR(30) NOT NULL,
        provider_room_key VARCHAR(255) NOT NULL,
        status VARCHAR(20) NOT NULL,
        started_by BIGINT NULL,
        started_at DATETIME2(7) NULL,
        ended_at DATETIME2(7) NULL,
        failure_code VARCHAR(80) NULL,
        concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_meeting_video_sessions_token DEFAULT NEWSEQUENTIALID(),
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_sessions_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_sessions_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT ck_meeting_video_sessions_status CHECK(status IN ('CREATED','LIVE','ENDED','FAILED')),
        CONSTRAINT fk_meeting_video_sessions_meeting FOREIGN KEY(meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_meeting_video_sessions_started_by FOREIGN KEY(started_by) REFERENCES dbo.users(id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_sessions') AND name=N'uq_meeting_video_sessions_room') CREATE UNIQUE INDEX uq_meeting_video_sessions_room ON dbo.meeting_video_sessions(provider, provider_room_key);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_sessions') AND name=N'ux_meeting_video_sessions_one_active') CREATE UNIQUE INDEX ux_meeting_video_sessions_one_active ON dbo.meeting_video_sessions(meeting_id) WHERE status IN ('CREATED','LIVE');
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_sessions') AND name=N'ix_meeting_video_sessions_meeting_status') CREATE INDEX ix_meeting_video_sessions_meeting_status ON dbo.meeting_video_sessions(meeting_id,status);

IF OBJECT_ID(N'dbo.meeting_video_participant_bindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_video_participant_bindings (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_participant_bindings PRIMARY KEY,
        meeting_video_session_id BIGINT NOT NULL,
        user_id BIGINT NOT NULL,
        provider_participant_identity VARCHAR(255) NOT NULL,
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_bindings_created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_meeting_video_bindings_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_video_bindings_user FOREIGN KEY(user_id) REFERENCES dbo.users(id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_participant_bindings') AND name=N'ux_meeting_video_bindings_session_user') CREATE UNIQUE INDEX ux_meeting_video_bindings_session_user ON dbo.meeting_video_participant_bindings(meeting_video_session_id,user_id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_participant_bindings') AND name=N'ux_meeting_video_bindings_identity') CREATE UNIQUE INDEX ux_meeting_video_bindings_identity ON dbo.meeting_video_participant_bindings(provider_participant_identity);

IF OBJECT_ID(N'dbo.meeting_video_presence_sessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_video_presence_sessions (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_video_presence_sessions PRIMARY KEY,
        meeting_video_session_id BIGINT NOT NULL,
        user_id BIGINT NOT NULL,
        provider_participant_identity VARCHAR(255) NOT NULL,
        provider_connection_id VARCHAR(255) NULL,
        joined_at DATETIME2(7) NOT NULL,
        left_at DATETIME2(7) NULL,
        disconnect_reason NVARCHAR(255) NULL,
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_presence_created DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2(7) NOT NULL CONSTRAINT df_meeting_video_presence_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_meeting_video_presence_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_video_presence_user FOREIGN KEY(user_id) REFERENCES dbo.users(id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.meeting_video_presence_sessions') AND name=N'ix_meeting_video_presence_lookup') CREATE INDEX ix_meeting_video_presence_lookup ON dbo.meeting_video_presence_sessions(meeting_video_session_id,user_id,joined_at);

IF OBJECT_ID(N'dbo.video_provider_events', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.video_provider_events (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_video_provider_events PRIMARY KEY,
        provider VARCHAR(30) NOT NULL,
        provider_event_id VARCHAR(255) NOT NULL,
        event_type VARCHAR(80) NOT NULL,
        meeting_video_session_id BIGINT NULL,
        received_at DATETIME2(7) NOT NULL CONSTRAINT df_video_provider_events_received DEFAULT SYSUTCDATETIME(),
        processed_at DATETIME2(7) NULL,
        payload_hash CHAR(64) NOT NULL,
        processing_status VARCHAR(20) NOT NULL,
        error_code VARCHAR(80) NULL,
        CONSTRAINT fk_video_provider_events_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.video_provider_events') AND name=N'ux_video_provider_events_id') CREATE UNIQUE INDEX ux_video_provider_events_id ON dbo.video_provider_events(provider,provider_event_id);

IF OBJECT_ID(N'dbo.video_provider_cleanup_jobs', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.video_provider_cleanup_jobs (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_video_provider_cleanup_jobs PRIMARY KEY,
        meeting_video_session_id BIGINT NOT NULL,
        provider_room_key VARCHAR(255) NOT NULL,
        status VARCHAR(20) NOT NULL,
        attempt_count INT NOT NULL CONSTRAINT df_video_cleanup_attempts DEFAULT 0,
        next_attempt_at DATETIME2(7) NOT NULL,
        lease_token UNIQUEIDENTIFIER NULL,
        lease_until DATETIME2(7) NULL,
        last_error_code VARCHAR(80) NULL,
        created_at DATETIME2(7) NOT NULL CONSTRAINT df_video_cleanup_created DEFAULT SYSUTCDATETIME(),
        completed_at DATETIME2(7) NULL,
        CONSTRAINT ck_video_cleanup_status CHECK(status IN ('PENDING','PROCESSING','SUCCEEDED','FAILED')),
        CONSTRAINT fk_video_cleanup_session FOREIGN KEY(meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id) ON DELETE CASCADE
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.video_provider_cleanup_jobs') AND name=N'ix_video_cleanup_claim') CREATE INDEX ix_video_cleanup_claim ON dbo.video_provider_cleanup_jobs(status,next_attempt_at,lease_until);

COMMIT TRANSACTION;
