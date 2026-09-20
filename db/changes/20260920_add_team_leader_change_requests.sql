/* Adds mentor-approved team leader changes. Safe to run repeatedly. */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.team_leader_change_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_leader_change_requests (
        id                      BIGINT IDENTITY(1,1) NOT NULL,
        team_id                 BIGINT NOT NULL,
        project_id              BIGINT NOT NULL,
        requested_by            BIGINT NOT NULL,
        current_leader_user_id BIGINT NOT NULL,
        new_leader_user_id     BIGINT NOT NULL,
        mentor_profile_id      BIGINT NOT NULL,
        status                  NVARCHAR(20) NOT NULL CONSTRAINT df_team_leader_change_requests_status DEFAULT (N'PENDING'),
        request_message         NVARCHAR(2000) NULL,
        response_message        NVARCHAR(2000) NULL,
        requested_at            DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_requested_at DEFAULT (SYSUTCDATETIME()),
        responded_at            DATETIME2(0) NULL,
        created_at              DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_created_at DEFAULT (SYSUTCDATETIME()),
        updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_team_leader_change_requests_updated_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT pk_team_leader_change_requests PRIMARY KEY (id),
        CONSTRAINT ck_team_leader_change_requests_status CHECK (status IN (N'PENDING', N'APPROVED', N'REJECTED', N'CANCELLED')),
        CONSTRAINT fk_team_leader_change_requests_team FOREIGN KEY (team_id)
            REFERENCES dbo.teams(id) ON DELETE CASCADE ON UPDATE NO ACTION,
        CONSTRAINT fk_team_leader_change_requests_project FOREIGN KEY (project_id)
            REFERENCES dbo.projects(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT fk_team_leader_change_requests_requested_by FOREIGN KEY (requested_by)
            REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT fk_team_leader_change_requests_current_leader FOREIGN KEY (current_leader_user_id)
            REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT fk_team_leader_change_requests_new_leader FOREIGN KEY (new_leader_user_id)
            REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION,
        CONSTRAINT fk_team_leader_change_requests_mentor_profile FOREIGN KEY (mentor_profile_id)
            REFERENCES dbo.supervisor_profiles(id) ON DELETE NO ACTION ON UPDATE NO ACTION
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_team_leader_change_requests_pending'
    AND object_id = OBJECT_ID(N'dbo.team_leader_change_requests'))
    CREATE UNIQUE INDEX ux_team_leader_change_requests_pending
        ON dbo.team_leader_change_requests(team_id) WHERE status = N'PENDING';

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_team_leader_change_requests_team_status'
    AND object_id = OBJECT_ID(N'dbo.team_leader_change_requests'))
    CREATE INDEX ix_team_leader_change_requests_team_status
        ON dbo.team_leader_change_requests(team_id, status);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_team_leader_change_requests_mentor_status'
    AND object_id = OBJECT_ID(N'dbo.team_leader_change_requests'))
    CREATE INDEX ix_team_leader_change_requests_mentor_status
        ON dbo.team_leader_change_requests(mentor_profile_id, status);

COMMIT TRANSACTION;
