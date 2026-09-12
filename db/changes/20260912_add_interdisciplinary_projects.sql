-- Additive migration. Run in the intended AI-PMS database; no historical approvals are inferred.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.team_academic_configurations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_academic_configurations (
        team_id bigint NOT NULL CONSTRAINT pk_team_academic_configurations PRIMARY KEY,
        project_mode varchar(30) NOT NULL,
        primary_major_id bigint NULL,
        lead_department_id bigint NOT NULL,
        concurrency_token uniqueidentifier NOT NULL,
        CONSTRAINT fk_team_academic_team FOREIGN KEY (team_id) REFERENCES dbo.teams(id),
        CONSTRAINT fk_team_academic_primary FOREIGN KEY (primary_major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_team_academic_lead FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT ck_team_academic_mode CHECK (
            (project_mode = 'SINGLE_MAJOR' AND primary_major_id IS NOT NULL)
            OR (project_mode = 'INTERDISCIPLINARY' AND primary_major_id IS NULL))
    );
END;
IF OBJECT_ID(N'dbo.team_major_requirements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_major_requirements (
        team_id bigint NOT NULL,
        major_id bigint NOT NULL,
        min_members int NOT NULL,
        max_members int NOT NULL,
        responsibility nvarchar(1000) NOT NULL,
        CONSTRAINT pk_team_major_requirements PRIMARY KEY (team_id, major_id),
        CONSTRAINT fk_team_requirement_team FOREIGN KEY (team_id) REFERENCES dbo.team_academic_configurations(team_id),
        CONSTRAINT fk_team_requirement_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id),
        CONSTRAINT ck_team_requirement_quota CHECK (min_members >= 1 AND max_members >= min_members),
        CONSTRAINT ck_team_requirement_responsibility CHECK (LEN(LTRIM(RTRIM(responsibility))) > 0)
    );
END;
IF OBJECT_ID(N'dbo.project_registration_snapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_registration_snapshots (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_project_registration_snapshots PRIMARY KEY,
        project_id bigint NOT NULL,
        project_period_id bigint NOT NULL,
        lead_department_id bigint NOT NULL,
        submitted_by bigint NOT NULL,
        submitted_at datetime2(0) NOT NULL,
        snapshot_json nvarchar(max) NOT NULL,
        CONSTRAINT fk_project_registration_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_project_registration_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
        CONSTRAINT fk_project_registration_lead FOREIGN KEY (lead_department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_project_registration_submitter FOREIGN KEY (submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_project_registration_json CHECK (ISJSON(snapshot_json) = 1)
    );
    CREATE INDEX ix_project_registration_latest ON dbo.project_registration_snapshots(project_id, id);
END;
IF OBJECT_ID(N'dbo.project_department_decisions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_department_decisions (
        snapshot_id bigint NOT NULL,
        department_id bigint NOT NULL,
        decision varchar(20) NOT NULL,
        decided_by bigint NULL,
        decided_at datetime2(0) NULL,
        reason nvarchar(2000) NULL,
        CONSTRAINT pk_project_department_decisions PRIMARY KEY (snapshot_id, department_id),
        CONSTRAINT fk_project_decision_snapshot FOREIGN KEY (snapshot_id) REFERENCES dbo.project_registration_snapshots(id),
        CONSTRAINT fk_project_decision_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_project_decision_actor FOREIGN KEY (decided_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_project_decision_state CHECK (
            (decision = 'PENDING' AND decided_by IS NULL AND decided_at IS NULL)
            OR (decision IN ('APPROVED','REJECTED') AND decided_by IS NOT NULL AND decided_at IS NOT NULL)),
        CONSTRAINT ck_project_decision_reason CHECK (decision <> 'REJECTED' OR (reason IS NOT NULL AND LEN(LTRIM(RTRIM(reason))) > 0))
    );
END;
COMMIT TRANSACTION;
