-- Add only the defaults/checks found missing by the full shared metadata audit.
-- Existing rows, immutable snapshots and duplicate legacy FKs are left untouched.
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID('dbo.meetings') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.meetings'),'meeting_delivery_mode','ColumnId'))
 ALTER TABLE dbo.meetings ADD CONSTRAINT df_meetings_delivery_mode DEFAULT ('ONSITE') FOR meeting_delivery_mode;
IF NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID('dbo.meetings') AND parent_column_id=COLUMNPROPERTY(OBJECT_ID('dbo.meetings'),'video_channel','ColumnId'))
 ALTER TABLE dbo.meetings ADD CONSTRAINT df_meetings_video_channel DEFAULT ('NONE') FOR video_channel;
IF OBJECT_ID('dbo.ck_supervisor_requests_assignment_type','C') IS NULL
 ALTER TABLE dbo.supervisor_requests WITH CHECK ADD CONSTRAINT ck_supervisor_requests_assignment_type
 CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR'));
IF OBJECT_ID('dbo.ck_supervisor_assignments_assignment_type','C') IS NULL
 ALTER TABLE dbo.supervisor_assignments WITH CHECK ADD CONSTRAINT ck_supervisor_assignments_assignment_type
 CHECK (assignment_type IN ('PRIMARY','DISCIPLINE_MENTOR'));
COMMIT;
GO
