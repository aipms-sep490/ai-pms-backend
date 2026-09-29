SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.user_external_logins', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.user_external_logins (
            id bigint IDENTITY PRIMARY KEY,
            user_id bigint NOT NULL REFERENCES dbo.users(id),
            provider varchar(30) NOT NULL,
            subject varchar(255) COLLATE Latin1_General_100_BIN2 NOT NULL,
            email nvarchar(255) NOT NULL,
            created_at datetime2 NOT NULL,
            updated_at datetime2 NOT NULL,
            CONSTRAINT uq_external_login_subject UNIQUE(provider, subject),
            CONSTRAINT uq_external_login_user UNIQUE(user_id, provider)
        );
    END;
    IF OBJECT_ID(N'dbo.external_login_challenges', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.external_login_challenges (
            id uniqueidentifier NOT NULL PRIMARY KEY,
            purpose varchar(10) NOT NULL,
            user_id bigint NULL REFERENCES dbo.users(id),
            nonce_hash binary(64) NOT NULL,
            browser_hash binary(64) NOT NULL,
            created_at datetime2 NOT NULL,
            expires_at datetime2 NOT NULL,
            consumed_at datetime2 NULL,
            CONSTRAINT ck_external_challenge_purpose CHECK
                ((purpose='LOGIN' AND user_id IS NULL) OR (purpose='LINK' AND user_id IS NOT NULL))
        );
        CREATE INDEX ix_external_challenges_expiry ON dbo.external_login_challenges(expires_at);
    END;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
