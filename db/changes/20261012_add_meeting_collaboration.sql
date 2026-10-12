SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.meetings','series_id') IS NULL ALTER TABLE dbo.meetings ADD series_id uniqueidentifier NULL;
IF COL_LENGTH('dbo.meetings','occurrence_number') IS NULL ALTER TABLE dbo.meetings ADD occurrence_number int NULL;
IF COL_LENGTH('dbo.meetings','recurrence') IS NULL ALTER TABLE dbo.meetings ADD recurrence varchar(12) NOT NULL CONSTRAINT df_meeting_recurrence DEFAULT 'NONE';
IF COL_LENGTH('dbo.meetings','notes_version') IS NULL ALTER TABLE dbo.meetings ADD notes_version int NOT NULL CONSTRAINT df_meeting_notes_version DEFAULT 0;
IF COL_LENGTH('dbo.meetings','notes_locked_at') IS NULL ALTER TABLE dbo.meetings ADD notes_locked_at datetime2(0) NULL;
IF COL_LENGTH('dbo.meetings','notes_locked_by') IS NULL ALTER TABLE dbo.meetings ADD notes_locked_by bigint NULL;
IF COL_LENGTH('dbo.meeting_participants','rsvp_status') IS NULL
BEGIN
 ALTER TABLE dbo.meeting_participants ADD rsvp_status varchar(12) NOT NULL CONSTRAINT df_meeting_rsvp DEFAULT 'INVITED';
 EXEC(N'UPDATE dbo.meeting_participants SET rsvp_status=attendance_status WHERE attendance_status IN (''INVITED'',''ACCEPTED'',''DECLINED'')');
END;
IF COL_LENGTH('dbo.meeting_participants','rsvp_token') IS NULL ALTER TABLE dbo.meeting_participants ADD rsvp_token uniqueidentifier NOT NULL CONSTRAINT df_meeting_rsvp_token DEFAULT NEWID();
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_meeting_recurrence')
 EXEC(N'ALTER TABLE dbo.meetings ADD CONSTRAINT ck_meeting_recurrence CHECK(recurrence IN (''NONE'',''WEEKLY'',''BIWEEKLY'') AND ((series_id IS NULL AND occurrence_number IS NULL AND recurrence=''NONE'') OR (series_id IS NOT NULL AND occurrence_number IS NOT NULL AND occurrence_number BETWEEN 1 AND 12 AND (recurrence<>''NONE'' OR occurrence_number=1))))');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_meeting_notes_lock')
 EXEC(N'ALTER TABLE dbo.meetings ADD CONSTRAINT ck_meeting_notes_lock CHECK(notes_version>=0 AND ((notes_locked_at IS NULL AND notes_locked_by IS NULL) OR (notes_locked_at IS NOT NULL AND notes_locked_by IS NOT NULL AND notes_version>0)))');
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name='fk_meeting_notes_locker')
 EXEC(N'ALTER TABLE dbo.meetings ADD CONSTRAINT fk_meeting_notes_locker FOREIGN KEY(notes_locked_by) REFERENCES dbo.users(id)');
IF NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE name='ck_meeting_rsvp')
 EXEC(N'ALTER TABLE dbo.meeting_participants ADD CONSTRAINT ck_meeting_rsvp CHECK(rsvp_status IN (''INVITED'',''ACCEPTED'',''TENTATIVE'',''DECLINED''))');
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.meetings') AND name='ux_meeting_occurrence')
 EXEC(N'CREATE UNIQUE INDEX ux_meeting_occurrence ON dbo.meetings(series_id,occurrence_number) WHERE series_id IS NOT NULL');
IF OBJECT_ID('dbo.meeting_notes_versions','U') IS NULL
BEGIN
 CREATE TABLE dbo.meeting_notes_versions(
  id bigint IDENTITY PRIMARY KEY, meeting_id bigint NOT NULL, version int NOT NULL, is_locked bit NOT NULL,
  meeting_notes nvarchar(max) NULL, minutes nvarchar(max) NULL, decisions nvarchar(max) NULL, blockers nvarchar(max) NULL,
  governance_json nvarchar(max) NOT NULL, changed_by bigint NOT NULL, changed_at datetime2(0) NOT NULL,
  CONSTRAINT fk_notes_version_meeting FOREIGN KEY(meeting_id) REFERENCES dbo.meetings(id),
  CONSTRAINT fk_notes_version_actor FOREIGN KEY(changed_by) REFERENCES dbo.users(id),
  CONSTRAINT ck_notes_version CHECK(version>0 AND ISJSON(governance_json)=1),
  CONSTRAINT uq_notes_version UNIQUE(meeting_id,version));
END;
COMMIT;
GO
