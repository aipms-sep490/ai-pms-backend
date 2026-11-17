SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.user_avatars', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.user_avatars (
        user_id bigint NOT NULL CONSTRAINT pk_user_avatars PRIMARY KEY,
        storage_key varchar(80) NOT NULL,
        content_type varchar(30) NOT NULL
            CONSTRAINT ck_user_avatars_content_type CHECK (content_type IN ('image/png', 'image/jpeg')),
        size_bytes bigint NOT NULL
            CONSTRAINT ck_user_avatars_size CHECK (size_bytes > 0 AND size_bytes <= 2097152),
        sha256 char(64) NOT NULL,
        concurrency_token uniqueidentifier NOT NULL,
        updated_at datetime2(0) NOT NULL,
        CONSTRAINT fk_user_avatars_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX ux_user_avatars_storage_key ON dbo.user_avatars(storage_key);
END;
COMMIT;
