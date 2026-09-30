/* Password recovery outbox. Additive and safe to rerun. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.users', N'password_recovery_invalid_before') IS NULL
    ALTER TABLE dbo.users ADD password_recovery_invalid_before DATETIME2(7) NULL;
IF OBJECT_ID(N'dbo.password_recovery_requests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.password_recovery_requests (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_password_recovery_requests PRIMARY KEY,
        email_hash VARCHAR(64) NOT NULL,
        protected_payload NVARCHAR(MAX) NULL,
        status VARCHAR(20) NOT NULL,
        created_at DATETIME2(7) NOT NULL,
        expires_at DATETIME2(7) NOT NULL,
        next_attempt_at DATETIME2(7) NOT NULL,
        attempt_count INT NOT NULL CONSTRAINT df_password_recovery_attempts DEFAULT(0),
        lease_token UNIQUEIDENTIFIER NULL,
        lease_until DATETIME2(7) NULL,
        reset_token_id BIGINT NULL CONSTRAINT fk_password_recovery_token REFERENCES dbo.password_reset_tokens(id),
        completed_at DATETIME2(7) NULL,
        error_code VARCHAR(40) NULL,
        CONSTRAINT ck_password_recovery_status CHECK(status IN ('PENDING','SENDING','RETRY','SENT','SKIPPED','SUPERSEDED','EXPIRED','FAILED')),
        CONSTRAINT ck_password_recovery_attempts CHECK(attempt_count >= 0),
        CONSTRAINT ck_password_recovery_expiry CHECK(expires_at > created_at)
    );
END;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.password_recovery_requests') AND name=N'ix_password_recovery_claim')
    CREATE INDEX ix_password_recovery_claim ON dbo.password_recovery_requests(status,next_attempt_at,lease_until);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.password_recovery_requests') AND name=N'ix_password_recovery_email')
    CREATE INDEX ix_password_recovery_email ON dbo.password_recovery_requests(email_hash,id DESC);
COMMIT TRANSACTION;
