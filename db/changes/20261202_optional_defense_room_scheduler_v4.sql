SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- PR-P2-B: Optional Admin Defense Room Scheduler (V4 45m/60m, 15m Break, Cold Buffer, Tech Check, Standby Pool)
-- Strictly non-blocking: enable_defense_room_scheduling defaults to 0 (false); defense_session_id remains nullable.

IF COL_LENGTH(N'dbo.project_periods', N'cold_window_end_at') IS NULL
    ALTER TABLE dbo.project_periods ADD cold_window_end_at datetime2(0) NULL;

IF COL_LENGTH(N'dbo.project_periods', N'defense_buffer_hours') IS NULL
    ALTER TABLE dbo.project_periods ADD defense_buffer_hours int NOT NULL CONSTRAINT df_project_periods_defense_buffer DEFAULT 12;

IF OBJECT_ID(N'dbo.rooms', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'dbo.rooms', N'room_cluster_type') IS NULL
        EXEC(N'ALTER TABLE dbo.rooms ADD room_cluster_type varchar(30) NULL');

    IF COL_LENGTH(N'dbo.rooms', N'equipment_tags_json') IS NULL
        EXEC(N'ALTER TABLE dbo.rooms ADD equipment_tags_json nvarchar(1000) NULL');

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_rooms_cluster_type')
        EXEC(N'ALTER TABLE dbo.rooms ADD CONSTRAINT ck_rooms_cluster_type CHECK (room_cluster_type IS NULL OR room_cluster_type IN (''IT'',''MKT'',''INTERDISCIPLINARY''))');
END;

IF COL_LENGTH(N'dbo.defense_sessions', N'schedule_policy_version') IS NULL
    ALTER TABLE dbo.defense_sessions ADD schedule_policy_version varchar(30) NOT NULL CONSTRAINT df_defense_sessions_policy DEFAULT 'LEGACY_75M';

IF COL_LENGTH(N'dbo.defense_sessions', N'slot_duration_minutes') IS NULL
    ALTER TABLE dbo.defense_sessions ADD slot_duration_minutes int NOT NULL CONSTRAINT df_defense_sessions_duration DEFAULT 75;

IF OBJECT_ID(N'dbo.rooms', N'U') IS NOT NULL AND OBJECT_ID(N'dbo.defense_tech_checks', N'U') IS NULL
BEGIN
    EXEC(N'CREATE TABLE dbo.defense_tech_checks (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_defense_tech_checks PRIMARY KEY,
        project_id bigint NOT NULL,
        room_id bigint NOT NULL,
        checked_by bigint NOT NULL,
        status varchar(20) NOT NULL CONSTRAINT df_defense_tech_checks_status DEFAULT ''PENDING'',
        notes nvarchar(1000) NULL,
        checked_at datetime2(0) NOT NULL CONSTRAINT df_defense_tech_checks_checked_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT ck_defense_tech_checks_status CHECK (status IN (''PENDING'',''PASSED'',''FAILED'')),
        CONSTRAINT fk_defense_tech_checks_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_defense_tech_checks_room FOREIGN KEY (room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT fk_defense_tech_checks_checker FOREIGN KEY (checked_by) REFERENCES dbo.users(id)
    )');
END;

IF OBJECT_ID(N'dbo.defense_standby_pool', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.defense_standby_pool (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_defense_standby_pool PRIMARY KEY,
        project_period_id bigint NOT NULL,
        user_id bigint NOT NULL,
        major_id bigint NOT NULL,
        department_id bigint NOT NULL,
        is_available bit NOT NULL CONSTRAINT df_defense_standby_available DEFAULT 1,
        created_at datetime2(0) NOT NULL CONSTRAINT df_defense_standby_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_defense_standby_pool_entry UNIQUE (project_period_id, user_id, major_id),
        CONSTRAINT fk_defense_standby_period FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id),
        CONSTRAINT fk_defense_standby_user FOREIGN KEY (user_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_defense_standby_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_defense_standby_dept FOREIGN KEY (department_id) REFERENCES dbo.departments(id)
    );
END;

COMMIT TRANSACTION;
GO
