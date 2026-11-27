SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.users', 'must_change_password') IS NULL
BEGIN
    ALTER TABLE dbo.users
        ADD must_change_password BIT NOT NULL
            CONSTRAINT df_users_must_change_password DEFAULT (0);
END
GO
