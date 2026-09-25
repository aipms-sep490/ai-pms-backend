/* Additive mentor metadata. Existing requests/assignments remain PRIMARY. */
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF COL_LENGTH(N'dbo.supervisor_requests', N'assignment_type') IS NULL
        ALTER TABLE dbo.supervisor_requests ADD assignment_type varchar(30) NOT NULL
            CONSTRAINT df_supervisor_requests_assignment_type DEFAULT ('PRIMARY') WITH VALUES;
    IF COL_LENGTH(N'dbo.supervisor_requests', N'major_id') IS NULL
        ALTER TABLE dbo.supervisor_requests ADD major_id bigint NULL;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'assignment_type') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD assignment_type varchar(30) NOT NULL
            CONSTRAINT df_supervisor_assignments_assignment_type DEFAULT ('PRIMARY') WITH VALUES;
    IF COL_LENGTH(N'dbo.supervisor_assignments', N'major_id') IS NULL
        ALTER TABLE dbo.supervisor_assignments ADD major_id bigint NULL;
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_requests_major')
        ALTER TABLE dbo.supervisor_requests ADD CONSTRAINT fk_supervisor_requests_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id);
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_supervisor_assignments_major')
        ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT fk_supervisor_assignments_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id);
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_supervisor_requests_assignment_type')
        ALTER TABLE dbo.supervisor_requests ADD CONSTRAINT ck_supervisor_requests_assignment_type CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR'));
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_supervisor_assignments_assignment_type')
        ALTER TABLE dbo.supervisor_assignments ADD CONSTRAINT ck_supervisor_assignments_assignment_type CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR'));
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ux_supervisor_assignments_active_major_mentor')
        CREATE UNIQUE INDEX ux_supervisor_assignments_active_major_mentor ON dbo.supervisor_assignments(project_id, major_id)
            WHERE assignment_type = 'DISCIPLINE_MENTOR' AND ended_at IS NULL;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
