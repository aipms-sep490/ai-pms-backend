SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.task_dependency_impacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.task_dependency_impacts
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_task_dependency_impacts PRIMARY KEY,
        task_id bigint NOT NULL,
        predecessor_task_id bigint NOT NULL,
        reopen_history_id bigint NOT NULL,
        status varchar(24) NOT NULL,
        reopened_at datetime2(7) NOT NULL,
        reopened_by bigint NOT NULL,
        reopen_reason nvarchar(1000) NOT NULL,
        reviewed_at datetime2(7) NULL,
        reviewed_by bigint NULL,
        review_reason nvarchar(1000) NULL,
        CONSTRAINT fk_task_dependency_impacts_task FOREIGN KEY (task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_task_dependency_impacts_predecessor FOREIGN KEY (predecessor_task_id) REFERENCES dbo.tasks(id),
        CONSTRAINT fk_task_dependency_impacts_history FOREIGN KEY (reopen_history_id) REFERENCES dbo.task_status_history(id),
        CONSTRAINT fk_task_dependency_impacts_reopened_by FOREIGN KEY (reopened_by) REFERENCES dbo.users(id),
        CONSTRAINT fk_task_dependency_impacts_reviewed_by FOREIGN KEY (reviewed_by) REFERENCES dbo.users(id),
        CONSTRAINT ck_task_dependency_impacts_status CHECK (status IN ('PENDING','CONFIRMED_VALID','REWORK_REQUIRED')),
        CONSTRAINT ck_task_dependency_impacts_review CHECK (
            (status = 'PENDING' AND reviewed_at IS NULL AND reviewed_by IS NULL AND review_reason IS NULL)
            OR (status <> 'PENDING' AND reviewed_at IS NOT NULL AND reviewed_by IS NOT NULL AND review_reason IS NOT NULL AND LEN(LTRIM(RTRIM(review_reason))) > 0)),
        CONSTRAINT ck_task_dependency_impacts_reason CHECK (LEN(LTRIM(RTRIM(reopen_reason))) > 0),
        CONSTRAINT uq_task_dependency_impacts_reopen UNIQUE (task_id, reopen_history_id)
    );
    CREATE INDEX ix_task_dependency_impacts_pending ON dbo.task_dependency_impacts(task_id, status);
END;

COMMIT TRANSACTION;
