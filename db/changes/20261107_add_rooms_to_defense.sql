SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.rooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.rooms (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        code nvarchar(50) NOT NULL,
        building nvarchar(50) NOT NULL,
        floor int NOT NULL,
        capacity int NOT NULL CONSTRAINT df_room_capacity DEFAULT 40,
        status varchar(20) NOT NULL CONSTRAINT df_room_status DEFAULT 'ACTIVE',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_room_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL CONSTRAINT df_room_created DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_room_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_room_code UNIQUE(code),
        CONSTRAINT ck_room_capacity CHECK(capacity > 0),
        CONSTRAINT ck_room_status CHECK(status IN ('ACTIVE','MAINTENANCE','INACTIVE'))
    );
    CREATE INDEX ix_room_building_floor ON dbo.rooms(building, floor);
END;
IF COL_LENGTH(N'dbo.defense_sessions', N'room_id') IS NULL
    ALTER TABLE dbo.defense_sessions ADD room_id bigint NULL;

-- Preserve legacy room text while assigning stable room IDs. Ambiguous/blank names remain nullable.
INSERT INTO dbo.rooms(code, building, floor, capacity, status)
SELECT DISTINCT UPPER(LTRIM(RTRIM(s.room))), N'Chưa xác định', 0, 40, 'ACTIVE'
FROM dbo.defense_sessions s
WHERE LEN(LTRIM(RTRIM(s.room))) BETWEEN 1 AND 50
  AND NOT EXISTS (SELECT 1 FROM dbo.rooms r WHERE r.code = UPPER(LTRIM(RTRIM(s.room))));
UPDATE s SET room_id = r.id
FROM dbo.defense_sessions s
JOIN dbo.rooms r ON r.code = UPPER(LTRIM(RTRIM(s.room)))
WHERE s.room_id IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_defense_session_room')
    ALTER TABLE dbo.defense_sessions ADD CONSTRAINT fk_defense_session_room FOREIGN KEY(room_id) REFERENCES dbo.rooms(id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.defense_sessions') AND name = N'ix_defense_session_room_start')
    CREATE INDEX ix_defense_session_room_start ON dbo.defense_sessions(room_id, start_at);
COMMIT TRANSACTION;
