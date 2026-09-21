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
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_users_academic_profile_status')
    ALTER TABLE dbo.users ADD CONSTRAINT ck_users_academic_profile_status CHECK (academic_profile_status IN (N'PENDING', N'VERIFIED', N'REJECTED'));
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_users_academic_profile_reviewer')
    ALTER TABLE dbo.users ADD CONSTRAINT fk_users_academic_profile_reviewer FOREIGN KEY (academic_profile_reviewed_by) REFERENCES dbo.users(id);
UPDATE u SET academic_profile_status = N'VERIFIED'
FROM dbo.users u JOIN dbo.majors m ON m.id = u.major_id AND m.is_active = 1
JOIN dbo.departments d ON d.id = u.department_id AND d.is_active = 1 AND d.id = m.department_id
JOIN dbo.organizations o ON o.id = d.organization_id AND o.is_active = 1
WHERE u.major_id IS NOT NULL AND u.department_id IS NOT NULL AND u.academic_profile_status = N'PENDING';
COMMIT TRANSACTION;
