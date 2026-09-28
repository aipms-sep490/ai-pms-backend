-- Additive migration: Team Eligibility Snapshots Governance
SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Table 1: Immutable Team Eligibility Check Snapshots
IF OBJECT_ID(N'dbo.team_eligibility_checks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_eligibility_checks (
        id                   BIGINT IDENTITY(1,1) NOT NULL,
        team_id              BIGINT NOT NULL,
        project_period_id    BIGINT NOT NULL,
        project_id           BIGINT NULL,
        round_type           VARCHAR(20) NOT NULL,
        revision_history_id  BIGINT NULL,
        project_mode         VARCHAR(30) NOT NULL,
        policy_version       NVARCHAR(100) NOT NULL,
        rule_version         VARCHAR(50) NOT NULL,
        roster_hash          VARCHAR(64) NOT NULL,
        academic_scope_hash  VARCHAR(64) NOT NULL,
        project_context_hash VARCHAR(64) NOT NULL,
        fingerprint          VARCHAR(64) NOT NULL,
        temporal_state_hash  VARCHAR(64) NOT NULL,
        evaluation_key       VARCHAR(64) NOT NULL,
        result               VARCHAR(10) NOT NULL,
        valid_until_at       DATETIME2(0) NULL,
        checked_by           BIGINT NOT NULL,
        checked_at           DATETIME2(0) NOT NULL CONSTRAINT df_team_eligibility_checks_checked_at DEFAULT (SYSUTCDATETIME()),
        trigger_source       VARCHAR(30) NOT NULL,

        CONSTRAINT pk_team_eligibility_checks PRIMARY KEY (id),
        CONSTRAINT fk_team_eligibility_checks_team FOREIGN KEY (team_id) 
            REFERENCES dbo.teams(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_checks_period FOREIGN KEY (project_period_id) 
            REFERENCES dbo.project_periods(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_checks_project FOREIGN KEY (project_id) 
            REFERENCES dbo.projects(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_checks_revision_history FOREIGN KEY (revision_history_id)
            REFERENCES dbo.project_status_history(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_checks_user FOREIGN KEY (checked_by) 
            REFERENCES dbo.users(id) ON DELETE NO ACTION,
        CONSTRAINT ck_team_eligibility_checks_round_type CHECK (round_type IN ('FORMATION', 'INITIAL', 'REVISION')),
        CONSTRAINT ck_team_eligibility_checks_round_integrity CHECK (
            (round_type = 'FORMATION' AND project_id IS NULL AND revision_history_id IS NULL)
            OR (round_type = 'INITIAL' AND project_id IS NOT NULL AND revision_history_id IS NULL)
            OR (round_type = 'REVISION' AND project_id IS NOT NULL AND revision_history_id IS NOT NULL)
        ),
        CONSTRAINT ck_team_eligibility_checks_mode CHECK (project_mode IN ('SINGLE_MAJOR', 'INTERDISCIPLINARY')),
        CONSTRAINT ck_team_eligibility_checks_result CHECK (result IN ('PASS', 'FAIL')),
        CONSTRAINT ck_team_eligibility_checks_trigger CHECK (trigger_source IN ('MANUAL_CHECK', 'REFRESH_ALIAS'))
    );

    CREATE UNIQUE INDEX ux_team_eligibility_checks_team_evaluation_key
        ON dbo.team_eligibility_checks(team_id, evaluation_key);

    CREATE INDEX ix_team_eligibility_checks_round_lookup
        ON dbo.team_eligibility_checks(team_id, project_id, round_type, revision_history_id, id DESC);

    CREATE INDEX ix_team_eligibility_checks_team_fingerprint
        ON dbo.team_eligibility_checks(team_id, fingerprint, id DESC);
END;

-- Table 2: Granular Explainable Issues
IF OBJECT_ID(N'dbo.team_eligibility_issues', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_eligibility_issues (
        id                   BIGINT IDENTITY(1,1) NOT NULL,
        eligibility_check_id BIGINT NOT NULL,
        rule_code            VARCHAR(50) NOT NULL,
        severity             VARCHAR(10) NOT NULL CONSTRAINT df_team_eligibility_issues_severity DEFAULT ('ERROR'),
        major_id             BIGINT NULL,
        user_id              BIGINT NULL,
        expected_value       NVARCHAR(255) NULL,
        actual_value         NVARCHAR(255) NULL,
        message              NVARCHAR(1000) NOT NULL,

        CONSTRAINT pk_team_eligibility_issues PRIMARY KEY (id),
        CONSTRAINT fk_team_eligibility_issues_check FOREIGN KEY (eligibility_check_id) 
            REFERENCES dbo.team_eligibility_checks(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_issues_major FOREIGN KEY (major_id) 
            REFERENCES dbo.majors(id) ON DELETE NO ACTION,
        CONSTRAINT fk_team_eligibility_issues_user FOREIGN KEY (user_id) 
            REFERENCES dbo.users(id) ON DELETE NO ACTION,
        CONSTRAINT ck_team_eligibility_issues_severity CHECK (severity IN ('ERROR', 'WARNING'))
    );

    CREATE INDEX ix_team_eligibility_issues_check 
        ON dbo.team_eligibility_issues(eligibility_check_id);
END;

COMMIT TRANSACTION;
