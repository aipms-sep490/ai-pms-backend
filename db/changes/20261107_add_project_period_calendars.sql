SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.project_period_calendars', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.project_period_calendars (
        id bigint IDENTITY(1,1) NOT NULL PRIMARY KEY,
        project_period_id bigint NOT NULL,
        working_days_of_week int NOT NULL CONSTRAINT df_period_calendar_days DEFAULT 62,
        holidays_json nvarchar(max) NOT NULL CONSTRAINT df_period_calendar_holidays DEFAULT N'[]',
        concurrency_token uniqueidentifier NOT NULL CONSTRAINT df_period_calendar_token DEFAULT NEWID(),
        created_at datetime2(0) NOT NULL CONSTRAINT df_period_calendar_created DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_period_calendar_updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_period_calendar_period UNIQUE(project_period_id),
        CONSTRAINT fk_period_calendar_period FOREIGN KEY(project_period_id) REFERENCES dbo.project_periods(id) ON DELETE CASCADE,
        CONSTRAINT ck_period_calendar_days CHECK(working_days_of_week BETWEEN 1 AND 127),
        CONSTRAINT ck_period_calendar_holidays CHECK(ISJSON(holidays_json) = 1)
    );
END;
COMMIT TRANSACTION;
