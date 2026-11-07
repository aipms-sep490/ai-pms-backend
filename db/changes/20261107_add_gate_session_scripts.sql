IF COL_LENGTH('dbo.review_gate_assignments', 'session_role') IS NULL
    ALTER TABLE dbo.review_gate_assignments ADD session_role varchar(20) NOT NULL CONSTRAINT DF_gate_assignment_session_role DEFAULT 'MEMBER';

IF OBJECT_ID('dbo.gate_session_scripts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.gate_session_scripts (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        project_gate_instance_id bigint NOT NULL UNIQUE,
        mode varchar(20) NOT NULL,
        room_id bigint NULL,
        live_kit_room_name nvarchar(100) NULL,
        live_kit_session_id nvarchar(100) NULL,
        scheduled_start_at datetimeoffset(0) NOT NULL,
        scheduled_end_at datetimeoffset(0) NOT NULL,
        actual_start_at datetimeoffset(0) NULL,
        actual_end_at datetimeoffset(0) NULL,
        total_duration_minutes int NULL,
        presences_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_presences DEFAULT '[]',
        agenda_stages_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_agenda DEFAULT '[]',
        qa_log_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_qa DEFAULT '[]',
        remediation_items_json nvarchar(max) NOT NULL CONSTRAINT DF_gate_script_remediation DEFAULT '[]',
        general_notes nvarchar(max) NULL,
        verdict varchar(30) NOT NULL CONSTRAINT DF_gate_script_verdict DEFAULT 'PENDING',
        signed_by_secretary nvarchar(100) NULL,
        signed_by_chair nvarchar(100) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT DF_gate_script_token DEFAULT NEWID(),
        created_at datetimeoffset(0) NOT NULL CONSTRAINT DF_gate_script_created DEFAULT SYSDATETIMEOFFSET(),
        updated_at datetimeoffset(0) NOT NULL CONSTRAINT DF_gate_script_updated DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT FK_gate_script_gate FOREIGN KEY(project_gate_instance_id) REFERENCES dbo.project_gate_instances(id),
        CONSTRAINT FK_gate_script_room FOREIGN KEY(room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT CK_gate_script_mode CHECK (mode IN ('ONLINE','OFFLINE')),
        CONSTRAINT CK_gate_script_mode_target CHECK ((mode='ONLINE' AND live_kit_room_name IS NOT NULL AND room_id IS NULL) OR (mode='OFFLINE' AND room_id IS NOT NULL AND live_kit_room_name IS NULL))
    );
END;
