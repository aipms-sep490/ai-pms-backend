SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.team_academic_configurations', 'responsibility_version') IS NULL
    ALTER TABLE dbo.team_academic_configurations ADD responsibility_version UNIQUEIDENTIFIER NULL;
IF OBJECT_ID('dbo.team_major_responsibilities', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.team_major_responsibilities (
        id BIGINT IDENTITY PRIMARY KEY,
        team_id BIGINT NOT NULL, major_id BIGINT NOT NULL,
        content NVARCHAR(2000) NOT NULL, sort_order INT NOT NULL,
        concurrency_token UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        created_by BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_responsibility_requirement FOREIGN KEY(team_id,major_id) REFERENCES dbo.team_major_requirements(team_id,major_id) ON DELETE CASCADE,
        CONSTRAINT fk_responsibility_actor FOREIGN KEY(created_by) REFERENCES dbo.users(id),
        CONSTRAINT uq_responsibility_order UNIQUE(team_id,major_id,sort_order),
        CONSTRAINT ck_responsibility_content CHECK(LEN(LTRIM(RTRIM(content)))>0 AND sort_order>=0)
    );
END;
IF OBJECT_ID('dbo.task_disciplines', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_disciplines (
        task_id BIGINT NOT NULL, major_id BIGINT NOT NULL,
        role VARCHAR(20) NOT NULL,
        created_by BIGINT NOT NULL, created_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT pk_task_disciplines PRIMARY KEY(task_id,major_id),
        CONSTRAINT fk_task_discipline_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id) ON DELETE CASCADE,
        CONSTRAINT fk_task_discipline_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_task_discipline_actor FOREIGN KEY(created_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_task_discipline_role CHECK(role IN ('PRIMARY','SUPPORTING'))
    );
    CREATE UNIQUE INDEX uq_task_discipline_primary ON dbo.task_disciplines(task_id) WHERE role='PRIMARY';
    CREATE INDEX ix_task_discipline_major ON dbo.task_disciplines(major_id,task_id);
END;
IF OBJECT_ID('dbo.project_evidence', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_evidence (
        id BIGINT IDENTITY PRIMARY KEY,
        project_id BIGINT NOT NULL, major_id BIGINT NULL,
        source_type VARCHAR(30) NOT NULL, source_id BIGINT NOT NULL,
        task_id BIGINT NULL, deliverable_id BIGINT NULL, meeting_id BIGINT NULL, progress_report_id BIGINT NULL, file_id BIGINT NULL,
        notes NVARCHAR(2000) NULL,
        verification_status VARCHAR(20) NOT NULL DEFAULT 'PENDING',
        submitted_by BIGINT NOT NULL, submitted_at DATETIME2(7) NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT fk_evidence_project FOREIGN KEY(project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_evidence_major FOREIGN KEY(major_id) REFERENCES dbo.majors(id),
        CONSTRAINT fk_evidence_actor FOREIGN KEY(submitted_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_evidence_task FOREIGN KEY(task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_evidence_deliverable FOREIGN KEY(deliverable_id) REFERENCES dbo.deliverables(id),
        CONSTRAINT fk_evidence_meeting FOREIGN KEY(meeting_id) REFERENCES dbo.meetings(id),
        CONSTRAINT fk_evidence_report FOREIGN KEY(progress_report_id) REFERENCES dbo.progress_reports(id),
        CONSTRAINT fk_evidence_file FOREIGN KEY(file_id) REFERENCES dbo.files(id),
        CONSTRAINT uq_evidence_source UNIQUE(project_id,source_type,source_id,major_id),
        CONSTRAINT ck_evidence_status CHECK(verification_status IN ('PENDING','UNKNOWN')),
        CONSTRAINT ck_evidence_source CHECK(
            (CASE WHEN task_id IS NULL THEN 0 ELSE 1 END + CASE WHEN deliverable_id IS NULL THEN 0 ELSE 1 END +
             CASE WHEN meeting_id IS NULL THEN 0 ELSE 1 END + CASE WHEN progress_report_id IS NULL THEN 0 ELSE 1 END +
             CASE WHEN file_id IS NULL THEN 0 ELSE 1 END)=1 AND
            ((source_type='TASK' AND task_id IS NOT NULL AND source_id=task_id) OR
             (source_type='DELIVERABLE' AND deliverable_id IS NOT NULL AND source_id=deliverable_id) OR
             (source_type='MEETING' AND meeting_id IS NOT NULL AND source_id=meeting_id) OR
             (source_type='PROGRESS_REPORT' AND progress_report_id IS NOT NULL AND source_id=progress_report_id) OR
             (source_type='FILE' AND file_id IS NOT NULL AND source_id=file_id)))
    );
    CREATE INDEX ix_evidence_project_time ON dbo.project_evidence(project_id,submitted_at DESC,id DESC);
    CREATE INDEX ix_evidence_project_major ON dbo.project_evidence(project_id,major_id,verification_status);
END;
-- Legacy tasks/files are not assigned guessed majors or verification outcomes.
COMMIT;
