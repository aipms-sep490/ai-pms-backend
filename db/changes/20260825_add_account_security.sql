/*
 AI-PMS database change script.
 Date: 2026-08-25
 Scope: BE-10 account security, permission mapping, token lifecycle and audit storage.

 This script is safe to rerun after a successful execution. It does not seed
 permissions and it never stores raw refresh/reset token values.
*/

USE [AI_PMS];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.roles', N'U') IS NULL
       OR OBJECT_ID(N'dbo.users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.user_roles', N'U') IS NULL
    BEGIN
        THROW 51200, 'BE-10 change requires dbo.roles, dbo.users and dbo.user_roles.', 1;
    END;

    /* Account lockout and password lifecycle state. */
    IF COL_LENGTH(N'dbo.users', N'access_failed_count') IS NULL
    BEGIN
        ALTER TABLE dbo.users
            ADD access_failed_count INT NOT NULL
                CONSTRAINT df_users_access_failed_count DEFAULT (0) WITH VALUES;
    END;

    IF COL_LENGTH(N'dbo.users', N'lockout_end_at') IS NULL
        ALTER TABLE dbo.users ADD lockout_end_at DATETIME2(0) NULL;

    IF COL_LENGTH(N'dbo.users', N'password_changed_at') IS NULL
        ALTER TABLE dbo.users ADD password_changed_at DATETIME2(0) NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.users')
          AND name = N'ck_users_access_failed_count'
    )
    BEGIN
        EXEC(N'
            ALTER TABLE dbo.users WITH CHECK
                ADD CONSTRAINT ck_users_access_failed_count
                    CHECK (access_failed_count >= 0);
        ');
    END;

    /* Permission catalog. */
    IF OBJECT_ID(N'dbo.permissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.permissions (
            id                      BIGINT IDENTITY(1,1) NOT NULL,
            code                    NVARCHAR(100) NOT NULL,
            name                    NVARCHAR(150) NOT NULL,
            description             NVARCHAR(500) NULL,
            is_system_permission    BIT NOT NULL CONSTRAINT df_permissions_is_system_permission DEFAULT (0),
            created_at              DATETIME2(0) NOT NULL CONSTRAINT df_permissions_created_at DEFAULT (SYSUTCDATETIME()),
            updated_at              DATETIME2(0) NOT NULL CONSTRAINT df_permissions_updated_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_permissions PRIMARY KEY (id),
            CONSTRAINT uq_permissions_code UNIQUE (code)
        );
    END;

    /* Record who assigned a user role without changing existing assignments. */
    IF COL_LENGTH(N'dbo.user_roles', N'assigned_by') IS NULL
        ALTER TABLE dbo.user_roles ADD assigned_by BIGINT NULL;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE parent_object_id = OBJECT_ID(N'dbo.user_roles')
          AND name = N'fk_user_roles_assigned_by'
    )
    BEGIN
        EXEC(N'
            ALTER TABLE dbo.user_roles WITH CHECK
                ADD CONSTRAINT fk_user_roles_assigned_by
                    FOREIGN KEY (assigned_by) REFERENCES dbo.users(id)
                    ON DELETE NO ACTION ON UPDATE NO ACTION;
        ');
    END;

    IF OBJECT_ID(N'dbo.role_permissions', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.role_permissions (
            role_id         BIGINT NOT NULL,
            permission_id   BIGINT NOT NULL,
            assigned_by     BIGINT NULL,
            assigned_at     DATETIME2(0) NOT NULL CONSTRAINT df_role_permissions_assigned_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
            CONSTRAINT fk_role_permissions_role FOREIGN KEY (role_id)
                REFERENCES dbo.roles(id) ON DELETE CASCADE ON UPDATE NO ACTION,
            CONSTRAINT fk_role_permissions_permission FOREIGN KEY (permission_id)
                REFERENCES dbo.permissions(id) ON DELETE CASCADE ON UPDATE NO ACTION,
            CONSTRAINT fk_role_permissions_assigned_by FOREIGN KEY (assigned_by)
                REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    /* Hashed refresh tokens provide rotation, revocation and reuse detection. */
    IF OBJECT_ID(N'dbo.refresh_tokens', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.refresh_tokens (
            id                   BIGINT IDENTITY(1,1) NOT NULL,
            user_id              BIGINT NOT NULL,
            token_hash           VARBINARY(64) NOT NULL,
            family_id            UNIQUEIDENTIFIER NOT NULL,
            expires_at           DATETIME2(0) NOT NULL,
            created_at           DATETIME2(0) NOT NULL CONSTRAINT df_refresh_tokens_created_at DEFAULT (SYSUTCDATETIME()),
            created_by_ip        NVARCHAR(45) NULL,
            user_agent           NVARCHAR(500) NULL,
            revoked_at           DATETIME2(0) NULL,
            revoked_by_ip        NVARCHAR(45) NULL,
            replaced_by_token_id BIGINT NULL,
            reuse_detected_at    DATETIME2(0) NULL,
            CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
            CONSTRAINT uq_refresh_tokens_token_hash UNIQUE (token_hash),
            CONSTRAINT ck_refresh_tokens_expires_at CHECK (expires_at > created_at),
            CONSTRAINT ck_refresh_tokens_revoked_at CHECK (revoked_at IS NULL OR revoked_at >= created_at),
            CONSTRAINT ck_refresh_tokens_reuse_detected_at CHECK (reuse_detected_at IS NULL OR reuse_detected_at >= created_at),
            CONSTRAINT fk_refresh_tokens_user FOREIGN KEY (user_id)
                REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION,
            CONSTRAINT fk_refresh_tokens_replaced_by FOREIGN KEY (replaced_by_token_id)
                REFERENCES dbo.refresh_tokens(id) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    /* One-time hashed password-reset tokens. */
    IF OBJECT_ID(N'dbo.password_reset_tokens', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.password_reset_tokens (
            id              BIGINT IDENTITY(1,1) NOT NULL,
            user_id         BIGINT NOT NULL,
            token_hash      VARBINARY(64) NOT NULL,
            expires_at      DATETIME2(0) NOT NULL,
            created_at      DATETIME2(0) NOT NULL CONSTRAINT df_password_reset_tokens_created_at DEFAULT (SYSUTCDATETIME()),
            requested_by_ip NVARCHAR(45) NULL,
            used_at         DATETIME2(0) NULL,
            CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
            CONSTRAINT uq_password_reset_tokens_token_hash UNIQUE (token_hash),
            CONSTRAINT ck_password_reset_tokens_expires_at CHECK (expires_at > created_at),
            CONSTRAINT ck_password_reset_tokens_used_at CHECK (used_at IS NULL OR used_at >= created_at),
            CONSTRAINT fk_password_reset_tokens_user FOREIGN KEY (user_id)
                REFERENCES dbo.users(id) ON DELETE CASCADE ON UPDATE NO ACTION
        );
    END;

    /* Append-only security and high-impact business audit trail. */
    IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.audit_logs (
            id              BIGINT IDENTITY(1,1) NOT NULL,
            actor_user_id   BIGINT NULL,
            action          NVARCHAR(100) NOT NULL,
            entity_type     NVARCHAR(100) NOT NULL,
            entity_id       NVARCHAR(100) NULL,
            outcome         NVARCHAR(20) NOT NULL CONSTRAINT df_audit_logs_outcome DEFAULT (N'SUCCESS'),
            correlation_id  UNIQUEIDENTIFIER NULL,
            ip_address      NVARCHAR(45) NULL,
            user_agent      NVARCHAR(500) NULL,
            details_json    NVARCHAR(MAX) NULL,
            occurred_at     DATETIME2(0) NOT NULL CONSTRAINT df_audit_logs_occurred_at DEFAULT (SYSUTCDATETIME()),
            CONSTRAINT pk_audit_logs PRIMARY KEY (id),
            CONSTRAINT ck_audit_logs_outcome CHECK (outcome IN (N'SUCCESS', N'FAILURE', N'DENIED')),
            CONSTRAINT ck_audit_logs_details_json CHECK (details_json IS NULL OR ISJSON(details_json) = 1),
            CONSTRAINT fk_audit_logs_actor FOREIGN KEY (actor_user_id)
                REFERENCES dbo.users(id) ON DELETE NO ACTION ON UPDATE NO ACTION
        );
    END;

    /* Supporting indexes; unique token hashes are declared with the tables. */
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.user_roles') AND name = N'ix_user_roles_assigned_by')
        EXEC(N'CREATE INDEX ix_user_roles_assigned_by ON dbo.user_roles(assigned_by) WHERE assigned_by IS NOT NULL;');

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.role_permissions') AND name = N'ix_role_permissions_permission_id')
        CREATE INDEX ix_role_permissions_permission_id ON dbo.role_permissions(permission_id);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.role_permissions') AND name = N'ix_role_permissions_assigned_by')
        CREATE INDEX ix_role_permissions_assigned_by ON dbo.role_permissions(assigned_by) WHERE assigned_by IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.refresh_tokens') AND name = N'ix_refresh_tokens_user_active')
        CREATE INDEX ix_refresh_tokens_user_active ON dbo.refresh_tokens(user_id, expires_at DESC) WHERE revoked_at IS NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.refresh_tokens') AND name = N'ix_refresh_tokens_family_id')
        CREATE INDEX ix_refresh_tokens_family_id ON dbo.refresh_tokens(family_id, created_at DESC);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.password_reset_tokens') AND name = N'ix_password_reset_tokens_user_active')
        CREATE INDEX ix_password_reset_tokens_user_active ON dbo.password_reset_tokens(user_id, expires_at DESC) WHERE used_at IS NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.audit_logs') AND name = N'ix_audit_logs_occurred_at')
        CREATE INDEX ix_audit_logs_occurred_at ON dbo.audit_logs(occurred_at DESC);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.audit_logs') AND name = N'ix_audit_logs_actor_occurred_at')
        CREATE INDEX ix_audit_logs_actor_occurred_at ON dbo.audit_logs(actor_user_id, occurred_at DESC) WHERE actor_user_id IS NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.audit_logs') AND name = N'ix_audit_logs_entity')
        CREATE INDEX ix_audit_logs_entity ON dbo.audit_logs(entity_type, entity_id, occurred_at DESC);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.audit_logs') AND name = N'ix_audit_logs_correlation_id')
        CREATE INDEX ix_audit_logs_correlation_id ON dbo.audit_logs(correlation_id) WHERE correlation_id IS NOT NULL;

    COMMIT TRANSACTION;
    PRINT N'BE-10 account-security database change completed successfully.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    PRINT N'BE-10 account-security database change failed and was rolled back.';
    THROW;
END CATCH;
GO
