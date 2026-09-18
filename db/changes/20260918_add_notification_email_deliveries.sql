IF OBJECT_ID(N'dbo.notification_email_deliveries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.notification_email_deliveries (
        notification_recipient_id BIGINT NOT NULL,
        status VARCHAR(20) NOT NULL CONSTRAINT df_notification_email_deliveries_status DEFAULT ('PENDING'),
        attempt_count INT NOT NULL CONSTRAINT df_notification_email_deliveries_attempts DEFAULT (0),
        next_attempt_at DATETIME2(0) NOT NULL CONSTRAINT df_notification_email_deliveries_next DEFAULT (SYSUTCDATETIME()),
        last_attempt_at DATETIME2(0) NULL,
        sent_at DATETIME2(0) NULL,
        last_error NVARCHAR(1000) NULL,
        CONSTRAINT pk_notification_email_deliveries PRIMARY KEY (notification_recipient_id),
        CONSTRAINT ck_notification_email_deliveries_status CHECK (status IN ('PENDING','SENDING','SENT','RETRY','FAILED')),
        CONSTRAINT fk_notification_email_deliveries_recipient FOREIGN KEY (notification_recipient_id)
            REFERENCES dbo.notification_recipients(id) ON DELETE CASCADE
    );
    CREATE INDEX ix_notification_email_deliveries_queue
        ON dbo.notification_email_deliveries(status, next_attempt_at);
END;
GO
