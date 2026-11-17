SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.defense_committees', N'concurrency_token') IS NULL
    ALTER TABLE dbo.defense_committees ADD concurrency_token uniqueidentifier NOT NULL
        CONSTRAINT df_defense_committee_token DEFAULT NEWID();
IF COL_LENGTH(N'dbo.defense_committee_members', N'role') IS NULL
    ALTER TABLE dbo.defense_committee_members ADD role varchar(20) NOT NULL
        CONSTRAINT df_defense_committee_member_role DEFAULT 'MEMBER';
IF OBJECT_ID(N'dbo.defense_schedule_change_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.defense_schedule_change_requests (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        defense_session_id bigint NOT NULL,
        requester_id bigint NOT NULL,
        current_room_id bigint NOT NULL,
        current_start_at datetimeoffset(0) NOT NULL,
        current_end_at datetimeoffset(0) NOT NULL,
        desired_room_id bigint NOT NULL,
        desired_start_at datetimeoffset(0) NOT NULL,
        desired_end_at datetimeoffset(0) NOT NULL,
        reason_category varchar(50) NOT NULL,
        reason_details nvarchar(2000) NOT NULL,
        evidence_file_url nvarchar(500) NULL,
        status varchar(30) NOT NULL CONSTRAINT df_defense_change_status DEFAULT 'PENDING',
        reviewer_id bigint NULL,
        review_note nvarchar(2000) NULL,
        reviewed_at datetimeoffset(0) NULL,
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_defense_change_token DEFAULT NEWID(),
        created_at datetimeoffset(0) NOT NULL CONSTRAINT df_defense_change_created DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT fk_defense_change_session FOREIGN KEY(defense_session_id) REFERENCES dbo.defense_sessions(id),
        CONSTRAINT fk_defense_change_requester FOREIGN KEY(requester_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_defense_change_reviewer FOREIGN KEY(reviewer_id) REFERENCES dbo.users(id),
        CONSTRAINT fk_defense_change_current_room FOREIGN KEY(current_room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT fk_defense_change_desired_room FOREIGN KEY(desired_room_id) REFERENCES dbo.rooms(id),
        CONSTRAINT ck_defense_change_status CHECK(status IN ('PENDING','APPROVED','REJECTED','WITHDRAWN')),
        CONSTRAINT ck_defense_change_reason CHECK(reason_category IN ('ROOM_FACILITY_ISSUE','SCHEDULE_CLASH','SPECIAL_REQUIREMENT','OTHER'))
    );
    CREATE INDEX ix_defense_change_session_status ON dbo.defense_schedule_change_requests(defense_session_id, status);
END;
COMMIT TRANSACTION;
