SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.meeting_transcript_segments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_transcript_segments
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_transcript_segments PRIMARY KEY,
        meeting_id bigint NOT NULL,
        meeting_video_session_id bigint NULL,
        speaker_user_id bigint NULL,
        speaker_name nvarchar(200) NOT NULL,
        language varchar(20) NOT NULL CONSTRAINT df_meeting_transcript_segments_language DEFAULT ('vi-VN'),
        source varchar(24) NOT NULL CONSTRAINT df_meeting_transcript_segments_source DEFAULT ('WEB_SPEECH'),
        content nvarchar(2000) NOT NULL,
        is_highlighted bit NOT NULL CONSTRAINT df_meeting_transcript_segments_highlighted DEFAULT (0),
        client_segment_id varchar(64) NULL,
        spoken_at datetime2(7) NOT NULL CONSTRAINT df_meeting_transcript_segments_spoken_at DEFAULT (SYSUTCDATETIME()),
        created_by bigint NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_meeting_transcript_segments_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_transcript_segments_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_transcript_segments_video_session FOREIGN KEY (meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id),
        CONSTRAINT fk_meeting_transcript_segments_speaker FOREIGN KEY (speaker_user_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_meeting_transcript_segments_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_meeting_transcript_segments_content CHECK (LEN(LTRIM(RTRIM(content))) > 0),
        CONSTRAINT ck_meeting_transcript_segments_speaker_name CHECK (LEN(LTRIM(RTRIM(speaker_name))) > 0),
        CONSTRAINT ck_meeting_transcript_segments_source CHECK (source IN ('WEB_SPEECH', 'AUDIO_STT', 'MANUAL_NOTE'))
    );
    CREATE INDEX ix_meeting_transcript_segments_meeting_spoken
        ON dbo.meeting_transcript_segments(meeting_id, spoken_at, id);
    CREATE UNIQUE INDEX uq_meeting_transcript_segments_client_id
        ON dbo.meeting_transcript_segments(meeting_id, client_segment_id)
        WHERE client_segment_id IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.meeting_ai_insights', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.meeting_ai_insights
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_meeting_ai_insights PRIMARY KEY,
        meeting_id bigint NOT NULL,
        meeting_video_session_id bigint NULL,
        insight_type varchar(32) NOT NULL,
        status varchar(20) NOT NULL,
        model_version nvarchar(120) NULL,
        transcript_segment_count int NOT NULL CONSTRAINT df_meeting_ai_insights_seg_count DEFAULT (0),
        last_segment_id bigint NULL,
        summary_text nvarchar(max) NOT NULL,
        payload_json nvarchar(max) NOT NULL,
        created_by bigint NOT NULL,
        created_at datetime2(7) NOT NULL CONSTRAINT df_meeting_ai_insights_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT fk_meeting_ai_insights_meeting FOREIGN KEY (meeting_id) REFERENCES dbo.meetings(id) ON DELETE CASCADE,
        CONSTRAINT fk_meeting_ai_insights_video_session FOREIGN KEY (meeting_video_session_id) REFERENCES dbo.meeting_video_sessions(id),
        CONSTRAINT fk_meeting_ai_insights_created_by FOREIGN KEY (created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_meeting_ai_insights_type CHECK (insight_type IN ('LIVE_PULSE', 'FULL_MINUTES', 'COACH_CHECK')),
        CONSTRAINT ck_meeting_ai_insights_status CHECK (status IN ('COMPLETED', 'FALLBACK'))
    );
    CREATE INDEX ix_meeting_ai_insights_meeting_created
        ON dbo.meeting_ai_insights(meeting_id, created_at DESC, id DESC);
END;

COMMIT TRANSACTION;
