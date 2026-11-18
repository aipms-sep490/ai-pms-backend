SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.milestone_dependencies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.milestone_dependencies (
        id BIGINT IDENTITY(1,1) NOT NULL,
        milestone_id BIGINT NOT NULL,
        depends_on_milestone_id BIGINT NOT NULL,
        dependency_type NVARCHAR(30) NOT NULL CONSTRAINT df_milestone_dependencies_type DEFAULT (N'FINISH_TO_START'),
        created_at DATETIME2(0) NOT NULL CONSTRAINT df_milestone_dependencies_created_at DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT pk_milestone_dependencies PRIMARY KEY (id),
        CONSTRAINT uq_milestone_dependencies_pair UNIQUE (milestone_id, depends_on_milestone_id),
        CONSTRAINT ck_milestone_dependencies_not_self CHECK (milestone_id <> depends_on_milestone_id),
        CONSTRAINT ck_milestone_dependencies_type CHECK (dependency_type = N'FINISH_TO_START'),
        CONSTRAINT fk_milestone_dependencies_milestone FOREIGN KEY (milestone_id)
            REFERENCES dbo.milestones(id) ON DELETE CASCADE,
        CONSTRAINT fk_milestone_dependencies_depends_on FOREIGN KEY (depends_on_milestone_id)
            REFERENCES dbo.milestones(id)
    );
END;

COMMIT TRANSACTION;
