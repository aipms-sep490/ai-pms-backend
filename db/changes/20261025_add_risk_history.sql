-- Apply after the base schema. Adds dbo.risk_history for risk analytics and audit trail.
IF OBJECT_ID(N'dbo.risk_history', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.risk_history (
        id BIGINT IDENTITY(1,1) NOT NULL,
        project_id BIGINT NOT NULL,
        risk_level NVARCHAR(50) NOT NULL,
        risk_score DECIMAL(5,2) NULL,
        overdue_tasks INT NOT NULL DEFAULT 0,
        blocked_tasks INT NOT NULL DEFAULT 0,
        unassigned_tasks INT NOT NULL DEFAULT 0,
        progress_percentage DECIMAL(5,2) NOT NULL DEFAULT 0,
        recorded_at DATETIME2 NOT NULL,
        CONSTRAINT pk_risk_history PRIMARY KEY (id),
        CONSTRAINT fk_risk_history_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id)
    );
    CREATE INDEX ix_risk_history_project_recorded ON dbo.risk_history (project_id, recorded_at DESC);
END;
GO
