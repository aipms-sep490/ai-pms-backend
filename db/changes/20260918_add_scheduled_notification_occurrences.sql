-- Apply after the base schema. No generated schema or application data is changed.
IF OBJECT_ID(N'dbo.scheduled_notification_occurrences', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.scheduled_notification_occurrences (
        project_id BIGINT NOT NULL,
        occurrence_key VARCHAR(180) NOT NULL,
        notification_id BIGINT NOT NULL,
        CONSTRAINT pk_scheduled_notification_occurrences PRIMARY KEY (project_id, occurrence_key),
        CONSTRAINT uq_scheduled_notification_occurrences_notification UNIQUE (notification_id),
        CONSTRAINT fk_scheduled_notification_occurrences_project FOREIGN KEY (project_id) REFERENCES dbo.projects(id),
        CONSTRAINT fk_scheduled_notification_occurrences_notification FOREIGN KEY (notification_id) REFERENCES dbo.notifications(id)
    );
END;
GO
