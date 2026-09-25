/* Repeatable, additive mentor lifecycle migration. Legacy rows retain their recorded primary flag. */
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.supervisor_requests', N'assignment_type') IS NULL
        ALTER TABLE dbo.supervisor_requests ADD assignment_type varchar(30) NOT NULL
            CONSTRAINT df_supervisor_requests_assignment_type DEFAULT ('PRIMARY') WITH VALUES;
    IF COL_LENGTH(N'dbo.supervisor_requests', N'major_id') IS NULL
        ALTER TABLE dbo.supervisor_requests ADD major_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_requests_major')
        EXEC(N'ALTER TABLE dbo.supervisor_requests ADD CONSTRAINT fk_supervisor_requests_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id)');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_supervisor_requests_assignment_slot')
        EXEC(N'ALTER TABLE dbo.supervisor_requests ADD CONSTRAINT ck_supervisor_requests_assignment_slot CHECK
            ((assignment_type = ''PRIMARY'' AND major_id IS NULL) OR
             (assignment_type = ''DISCIPLINE_MENTOR'' AND major_id IS NOT NULL))');
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'assignment_type') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD assignment_type varchar(30) NOT NULL
            CONSTRAINT df_supervisor_assignments_assignment_type DEFAULT ('PRIMARY') WITH VALUES;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'major_id') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD major_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_assignments_major')
        EXEC(N'ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT fk_supervisor_assignments_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id)');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_supervisor_assignments_assignment_slot')
        EXEC(N'ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT ck_supervisor_assignments_assignment_slot CHECK
            ((assignment_type = ''PRIMARY'' AND major_id IS NULL) OR
             (assignment_type = ''DISCIPLINE_MENTOR'' AND major_id IS NOT NULL AND is_primary = 0))');
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'assigned_by') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD assigned_by bigint NULL;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'ended_by') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD ended_by bigint NULL;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'end_reason') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD end_reason nvarchar(2000) NULL;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'replaces_assignment_id') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD replaces_assignment_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_assignments_assigned_by')
        EXEC(N'ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT fk_supervisor_assignments_assigned_by FOREIGN KEY (assigned_by) REFERENCES dbo.users(id)');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_assignments_ended_by')
        EXEC(N'ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT fk_supervisor_assignments_ended_by FOREIGN KEY (ended_by) REFERENCES dbo.users(id)');
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_assignments_replaces_assignment_id')
        EXEC(N'ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT fk_supervisor_assignments_replaces_assignment_id FOREIGN KEY (replaces_assignment_id) REFERENCES dbo.supervisor_assignments(id)');
    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'uq_supervisor_assignments_project_supervisor')
        ALTER TABLE dbo.supervisor_assignments DROP CONSTRAINT uq_supervisor_assignments_project_supervisor;
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_supervisor_requests_pending' AND object_id = OBJECT_ID(N'dbo.supervisor_requests'))
        DROP INDEX ux_supervisor_requests_pending ON dbo.supervisor_requests;
    EXEC(N'CREATE UNIQUE INDEX ux_supervisor_requests_pending ON dbo.supervisor_requests
        (project_id, supervisor_profile_id, assignment_type, major_id) WHERE status = N''PENDING''');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_supervisor_assignments_active_major_mentor' AND object_id = OBJECT_ID(N'dbo.supervisor_assignments'))
        EXEC(N'CREATE UNIQUE INDEX ux_supervisor_assignments_active_major_mentor ON dbo.supervisor_assignments(project_id, major_id)
            WHERE assignment_type = ''DISCIPLINE_MENTOR'' AND ended_at IS NULL');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_supervisor_assignments_replacement' AND object_id = OBJECT_ID(N'dbo.supervisor_assignments'))
        EXEC(N'CREATE UNIQUE INDEX ux_supervisor_assignments_replacement ON dbo.supervisor_assignments(replaces_assignment_id)
            WHERE replaces_assignment_id IS NOT NULL');
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
