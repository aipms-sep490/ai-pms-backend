SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Student administrative class (class_code) & department-scoped specialization catalog (department_specializations)

IF OBJECT_ID(N'dbo.department_specializations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.department_specializations (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT pk_department_specializations PRIMARY KEY,
        department_id bigint NOT NULL,
        major_id bigint NULL,
        code nvarchar(50) NOT NULL,
        name nvarchar(200) NOT NULL,
        description nvarchar(500) NULL,
        is_active bit NOT NULL CONSTRAINT df_department_specializations_is_active DEFAULT 1,
        created_at datetime2(0) NOT NULL CONSTRAINT df_department_specializations_created_at DEFAULT SYSUTCDATETIME(),
        updated_at datetime2(0) NOT NULL CONSTRAINT df_department_specializations_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_department_specializations_dept_code UNIQUE (department_id, code),
        CONSTRAINT fk_department_specializations_department FOREIGN KEY (department_id) REFERENCES dbo.departments(id),
        CONSTRAINT fk_department_specializations_major FOREIGN KEY (major_id) REFERENCES dbo.majors(id)
    );
    CREATE INDEX ix_department_specializations_dept_major ON dbo.department_specializations(department_id, major_id, is_active);
END;

IF COL_LENGTH(N'dbo.users', N'class_code') IS NULL
    ALTER TABLE dbo.users ADD class_code nvarchar(50) NULL;

IF COL_LENGTH(N'dbo.users', N'specialization_id') IS NULL
    ALTER TABLE dbo.users ADD specialization_id bigint NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_users_specialization')
    EXEC(N'ALTER TABLE dbo.users ADD CONSTRAINT fk_users_specialization FOREIGN KEY (specialization_id) REFERENCES dbo.department_specializations(id)');

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.users') AND name = N'ix_users_department_class_spec')
    EXEC(N'CREATE INDEX ix_users_department_class_spec ON dbo.users(department_id, class_code, specialization_id) WHERE department_id IS NOT NULL');

COMMIT TRANSACTION;
GO
