-- Additive migration: Team Eligibility Snapshots Governance
-- Self-healing, rerunnable, non-destructive migration.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- =========================================================
    -- Table 1: dbo.team_eligibility_checks
    -- =========================================================

    -- A. Table creation if not exists
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
            checked_at           DATETIME2(0) NOT NULL,
            trigger_source       VARCHAR(30) NOT NULL
        );
    END
    ELSE
    BEGIN
        -- B. Column compatibility verification if table already exists
        IF EXISTS (
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0 AND is_identity = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'team_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'project_period_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'project_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'round_type' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 20 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'revision_history_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'project_mode' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 30 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'policy_version' AND TYPE_NAME(user_type_id) = 'nvarchar' AND max_length >= 200 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'rule_version' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 50 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'roster_hash' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'academic_scope_hash' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'project_context_hash' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'fingerprint' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'temporal_state_hash' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'evaluation_key' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 64 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'result' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 10 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'valid_until_at' AND TYPE_NAME(user_type_id) = 'datetime2' AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'checked_by' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'checked_at' AND TYPE_NAME(user_type_id) = 'datetime2' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_checks') AND name = 'trigger_source' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 30 AND is_nullable = 0)
        )
        BEGIN
            THROW 50001, N'Existing table dbo.team_eligibility_checks has incompatible column definitions.', 1;
        END;
    END;

    -- C. Self-healing constraints for dbo.team_eligibility_checks
    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE type = 'PK'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT pk_team_eligibility_checks PRIMARY KEY (id);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints
        WHERE name = N'df_team_eligibility_checks_checked_at'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT df_team_eligibility_checks_checked_at DEFAULT (SYSUTCDATETIME()) FOR checked_at;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_checks_team'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT fk_team_eligibility_checks_team FOREIGN KEY (team_id)
                REFERENCES dbo.teams(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_checks_period'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT fk_team_eligibility_checks_period FOREIGN KEY (project_period_id)
                REFERENCES dbo.project_periods(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_checks_project'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT fk_team_eligibility_checks_project FOREIGN KEY (project_id)
                REFERENCES dbo.projects(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_checks_revision_history'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT fk_team_eligibility_checks_revision_history FOREIGN KEY (revision_history_id)
                REFERENCES dbo.project_status_history(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_checks_user'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT fk_team_eligibility_checks_user FOREIGN KEY (checked_by)
                REFERENCES dbo.users(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_checks_round_type'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT ck_team_eligibility_checks_round_type CHECK (round_type IN (''FORMATION'', ''INITIAL'', ''REVISION''));');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_checks_round_integrity'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT ck_team_eligibility_checks_round_integrity CHECK (
                (round_type = ''FORMATION'' AND project_id IS NULL AND revision_history_id IS NULL)
                OR (round_type = ''INITIAL'' AND project_id IS NOT NULL AND revision_history_id IS NULL)
                OR (round_type = ''REVISION'' AND project_id IS NOT NULL AND revision_history_id IS NOT NULL)
            );');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_checks_mode'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT ck_team_eligibility_checks_mode CHECK (project_mode IN (''SINGLE_MAJOR'', ''INTERDISCIPLINARY''));');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_checks_result'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT ck_team_eligibility_checks_result CHECK (result IN (''PASS'', ''FAIL''));');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_checks_trigger'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_checks
            ADD CONSTRAINT ck_team_eligibility_checks_trigger CHECK (trigger_source IN (''MANUAL_CHECK'', ''REFRESH_ALIAS''));');
    END;

    -- D. Self-healing indexes for dbo.team_eligibility_checks
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ux_team_eligibility_checks_team_evaluation_key'
          AND object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX ux_team_eligibility_checks_team_evaluation_key
            ON dbo.team_eligibility_checks(team_id, evaluation_key);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ix_team_eligibility_checks_round_lookup'
          AND object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'CREATE INDEX ix_team_eligibility_checks_round_lookup
            ON dbo.team_eligibility_checks(team_id, project_id, round_type, revision_history_id, id DESC);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ix_team_eligibility_checks_team_fingerprint'
          AND object_id = OBJECT_ID(N'dbo.team_eligibility_checks')
    )
    BEGIN
        EXEC(N'CREATE INDEX ix_team_eligibility_checks_team_fingerprint
            ON dbo.team_eligibility_checks(team_id, fingerprint, id DESC);');
    END;


    -- =========================================================
    -- Table 2: dbo.team_eligibility_issues
    -- =========================================================

    -- A. Table creation if not exists
    IF OBJECT_ID(N'dbo.team_eligibility_issues', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.team_eligibility_issues (
            id                   BIGINT IDENTITY(1,1) NOT NULL,
            eligibility_check_id BIGINT NOT NULL,
            sort_order           INT NOT NULL,
            rule_code            VARCHAR(50) NOT NULL,
            severity             VARCHAR(10) NOT NULL,
            major_id             BIGINT NULL,
            user_id              BIGINT NULL,
            expected_value       NVARCHAR(255) NULL,
            actual_value         NVARCHAR(255) NULL,
            message              NVARCHAR(1000) NOT NULL,
            created_at           DATETIME2(0) NOT NULL
        );
    END
    ELSE
    BEGIN
        -- B. Column compatibility verification if table already exists
        IF EXISTS (
            SELECT 1
            WHERE NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0 AND is_identity = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'eligibility_check_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'sort_order' AND TYPE_NAME(user_type_id) = 'int' AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'rule_code' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 50 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'severity' AND TYPE_NAME(user_type_id) = 'varchar' AND max_length >= 10 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'major_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'user_id' AND TYPE_NAME(user_type_id) = 'bigint' AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'expected_value' AND TYPE_NAME(user_type_id) = 'nvarchar' AND max_length >= 510 AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'actual_value' AND TYPE_NAME(user_type_id) = 'nvarchar' AND max_length >= 510 AND is_nullable = 1)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'message' AND TYPE_NAME(user_type_id) = 'nvarchar' AND max_length >= 2000 AND is_nullable = 0)
               OR NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.team_eligibility_issues') AND name = 'created_at' AND TYPE_NAME(user_type_id) = 'datetime2' AND is_nullable = 0)
        )
        BEGIN
            THROW 50002, N'Existing table dbo.team_eligibility_issues has incompatible column definitions.', 1;
        END;
    END;

    -- C. Self-healing constraints for dbo.team_eligibility_issues
    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE type = 'PK'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT pk_team_eligibility_issues PRIMARY KEY (id);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints
        WHERE name = N'df_team_eligibility_issues_severity'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT df_team_eligibility_issues_severity DEFAULT (''ERROR'') FOR severity;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints
        WHERE name = N'df_team_eligibility_issues_created_at'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT df_team_eligibility_issues_created_at DEFAULT (SYSUTCDATETIME()) FOR created_at;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_issues_check'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT fk_team_eligibility_issues_check FOREIGN KEY (eligibility_check_id)
                REFERENCES dbo.team_eligibility_checks(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_issues_major'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT fk_team_eligibility_issues_major FOREIGN KEY (major_id)
                REFERENCES dbo.majors(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'fk_team_eligibility_issues_user'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT fk_team_eligibility_issues_user FOREIGN KEY (user_id)
                REFERENCES dbo.users(id) ON DELETE NO ACTION;');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_issues_sort_order'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT ck_team_eligibility_issues_sort_order CHECK (sort_order >= 0);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'ck_team_eligibility_issues_severity'
          AND parent_object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'ALTER TABLE dbo.team_eligibility_issues
            ADD CONSTRAINT ck_team_eligibility_issues_severity CHECK (severity IN (''ERROR'', ''WARNING''));');
    END;

    -- D. Self-healing indexes for dbo.team_eligibility_issues
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ux_team_eligibility_issues_check_sort'
          AND object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'CREATE UNIQUE INDEX ux_team_eligibility_issues_check_sort
            ON dbo.team_eligibility_issues(eligibility_check_id, sort_order);');
    END;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'ix_team_eligibility_issues_check'
          AND object_id = OBJECT_ID(N'dbo.team_eligibility_issues')
    )
    BEGIN
        EXEC(N'CREATE INDEX ix_team_eligibility_issues_check
            ON dbo.team_eligibility_issues(eligibility_check_id);');
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
