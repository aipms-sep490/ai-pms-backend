IF OBJECT_ID(N'dbo.contribution_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.contribution_snapshots (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_contribution_snapshots PRIMARY KEY,
        project_id BIGINT NOT NULL,
        user_id BIGINT NOT NULL,
        snapshot_at DATETIME2(0) NOT NULL,
        snapshot_hash CHAR(64) NOT NULL,
        activity_score FLOAT NOT NULL,
        evidence_count INT NOT NULL,
        snapshot_json NVARCHAR(MAX) NOT NULL,
        CONSTRAINT fk_contribution_snapshots_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_contribution_snapshots_user FOREIGN KEY (user_id) REFERENCES dbo.users(id),
        CONSTRAINT ck_contribution_snapshots_json CHECK (ISJSON(snapshot_json) = 1)
    );
    CREATE UNIQUE INDEX ux_contribution_snapshots_hash ON dbo.contribution_snapshots(project_id, user_id, snapshot_hash);
END
