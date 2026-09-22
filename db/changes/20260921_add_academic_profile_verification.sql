SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.users', N'academic_profile_status') IS NULL
    ALTER TABLE dbo.users ADD academic_profile_status NVARCHAR(20) NOT NULL CONSTRAINT df_users_academic_profile_status DEFAULT (N'PENDING');
IF COL_LENGTH(N'dbo.users', N'academic_profile_reviewed_by') IS NULL
    ALTER TABLE dbo.users ADD academic_profile_reviewed_by BIGINT NULL;
IF COL_LENGTH(N'dbo.users', N'academic_profile_reviewed_at') IS NULL
    ALTER TABLE dbo.users ADD academic_profile_reviewed_at DATETIME2(0) NULL;
IF COL_LENGTH(N'dbo.users', N'academic_profile_rejection_reason') IS NULL
    ALTER TABLE dbo.users ADD academic_profile_rejection_reason NVARCHAR(2000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_users_academic_profile_status')
    ALTER TABLE dbo.users ADD CONSTRAINT ck_users_academic_profile_status CHECK (academic_profile_status IN (N'PENDING', N'VERIFIED', N'REJECTED'));
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_users_academic_profile_reviewer')
    ALTER TABLE dbo.users ADD CONSTRAINT fk_users_academic_profile_reviewer FOREIGN KEY (academic_profile_reviewed_by) REFERENCES dbo.users(id);
IF OBJECT_ID(N'dbo.academic_profile_verifications', N'U') IS NULL
BEGIN
UPDATE u SET academic_profile_status = N'VERIFIED'
FROM dbo.users u JOIN dbo.majors m ON m.id = u.major_id AND m.is_active = 1
JOIN dbo.departments d ON d.id = u.department_id AND d.is_active = 1 AND d.id = m.department_id
JOIN dbo.organizations o ON o.id = d.organization_id AND o.is_active = 1
WHERE u.status = N'ACTIVE' AND EXISTS (SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id = ur.role_id WHERE ur.user_id = u.id AND r.code = N'STUDENT') AND u.major_id IS NOT NULL AND u.department_id IS NOT NULL AND u.academic_profile_status = N'PENDING';
CREATE TABLE dbo.academic_profile_verifications (
    id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_academic_profile_verifications PRIMARY KEY,
    user_id BIGINT NOT NULL,
    status NVARCHAR(20) NOT NULL,
    reviewed_by BIGINT NULL,
    reviewed_at DATETIME2(0) NULL,
    rejection_reason NVARCHAR(2000) NULL,
    CONSTRAINT ck_academic_profile_verifications_status CHECK (status IN (N'PENDING', N'VERIFIED', N'REJECTED')),
    CONSTRAINT ck_academic_profile_verifications_reason CHECK (status <> N'REJECTED' OR (rejection_reason IS NOT NULL AND LEN(LTRIM(RTRIM(rejection_reason))) > 0)),
    CONSTRAINT fk_academic_profile_verifications_user FOREIGN KEY (user_id) REFERENCES dbo.users(id),
    CONSTRAINT fk_academic_profile_verifications_reviewer FOREIGN KEY (reviewed_by) REFERENCES dbo.users(id)
);
CREATE INDEX ix_academic_profile_verifications_user ON dbo.academic_profile_verifications(user_id, id);
INSERT dbo.academic_profile_verifications(user_id, status)
SELECT u.id, u.academic_profile_status FROM dbo.users u WHERE EXISTS
(SELECT 1 FROM dbo.user_roles ur JOIN dbo.roles r ON r.id = ur.role_id WHERE ur.user_id = u.id AND r.code = N'STUDENT');
END;
COMMIT TRANSACTION;
