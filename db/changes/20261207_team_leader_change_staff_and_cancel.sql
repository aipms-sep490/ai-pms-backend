-- TK-W1-12: Allow team leader change requests without an assigned primary mentor (routed to Lead Dept),
-- track deciding actor & decision source (MENTOR, DEPARTMENT_STAFF, DEPARTMENT_FORCE), and support cancel.
SET XACT_ABORT ON;

IF EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.tables t ON t.object_id = c.object_id
    WHERE t.name = N'team_leader_change_requests'
      AND c.name = N'mentor_profile_id'
      AND c.is_nullable = 0
)
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ix_team_leader_change_requests_mentor_status'
          AND object_id = OBJECT_ID(N'dbo.team_leader_change_requests')
    )
    BEGIN
        DROP INDEX ix_team_leader_change_requests_mentor_status ON dbo.team_leader_change_requests;
    END;

    ALTER TABLE dbo.team_leader_change_requests
        ALTER COLUMN mentor_profile_id BIGINT NULL;

    CREATE INDEX ix_team_leader_change_requests_mentor_status
        ON dbo.team_leader_change_requests(mentor_profile_id, status);
END;

IF COL_LENGTH(N'dbo.team_leader_change_requests', N'decided_by_user_id') IS NULL
BEGIN
    ALTER TABLE dbo.team_leader_change_requests
        ADD decided_by_user_id BIGINT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'fk_team_leader_change_requests_decided_by'
      AND parent_object_id = OBJECT_ID(N'dbo.team_leader_change_requests')
)
BEGIN
    ALTER TABLE dbo.team_leader_change_requests
        ADD CONSTRAINT fk_team_leader_change_requests_decided_by FOREIGN KEY (decided_by_user_id)
            REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION;
END;
GO

IF COL_LENGTH(N'dbo.team_leader_change_requests', N'decision_source') IS NULL
BEGIN
    ALTER TABLE dbo.team_leader_change_requests
        ADD decision_source NVARCHAR(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'ck_team_leader_change_requests_decision_source'
      AND parent_object_id = OBJECT_ID(N'dbo.team_leader_change_requests')
)
BEGIN
    ALTER TABLE dbo.team_leader_change_requests
        ADD CONSTRAINT ck_team_leader_change_requests_decision_source
            CHECK (decision_source IS NULL OR decision_source IN (N'MENTOR', N'DEPARTMENT_STAFF', N'DEPARTMENT_FORCE'));
END;
GO
