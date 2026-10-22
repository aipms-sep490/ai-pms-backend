SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.notification_recipients', 'realtime_delivered_at') IS NULL
    ALTER TABLE dbo.notification_recipients ADD realtime_delivered_at DATETIME2(0) NULL;

UPDATE dbo.notification_recipients
SET realtime_delivered_at = COALESCE(updated_at, created_at)
WHERE realtime_delivered_at IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'ix_notification_recipients_realtime_pending'
    AND object_id = OBJECT_ID(N'dbo.notification_recipients'))
    CREATE INDEX ix_notification_recipients_realtime_pending
        ON dbo.notification_recipients(realtime_delivered_at, id)
        WHERE realtime_delivered_at IS NULL;

COMMIT;
