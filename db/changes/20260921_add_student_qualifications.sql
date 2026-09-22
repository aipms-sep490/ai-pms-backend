-- Student project-participation qualification and period policy.
-- Additive/rerunnable. Apply after db/schema.sql and existing 202609xx change scripts.
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.student_qualifications', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.student_qualifications
        (
            id BIGINT IDENTITY(1,1) NOT NULL,
            user_id BIGINT NOT NULL,
            organization_id BIGINT NOT NULL,
            qualification_type VARCHAR(50) NOT NULL,
            training_status VARCHAR(30) NOT NULL,
            verification_status VARCHAR(30) NOT NULL,
            certificate_number NVARCHAR(100) NULL,
            certificate_file_id BIGINT NULL,
            issued_at DATETIME2(0) NULL,
            expires_at DATETIME2(0) NULL,
            verified_by BIGINT NULL,
            verified_at DATETIME2(0) NULL,
            rejection_reason NVARCHAR(1000) NULL,
            concurrency_token UNIQUEIDENTIFIER NOT NULL CONSTRAINT df_student_qualifications_token DEFAULT NEWID(),
            created_at DATETIME2(0) NOT NULL CONSTRAINT df_student_qualifications_created DEFAULT SYSUTCDATETIME(),
            updated_at DATETIME2(0) NOT NULL CONSTRAINT df_student_qualifications_updated DEFAULT SYSUTCDATETIME(),
            CONSTRAINT pk_student_qualifications PRIMARY KEY (id),
            CONSTRAINT uq_student_qualifications_user_type UNIQUE (user_id, qualification_type),
            CONSTRAINT ck_student_qualifications_training_status CHECK (training_status IN ('PENDING_TRAINING','TRAINING_COMPLETED')),
            CONSTRAINT ck_student_qualifications_verification_status CHECK (verification_status IN ('PENDING_VERIFICATION','VERIFIED','REJECTED','EXPIRED')),
            CONSTRAINT fk_student_qualifications_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
            CONSTRAINT fk_student_qualifications_organization FOREIGN KEY (organization_id) REFERENCES dbo.organizations(id),
            CONSTRAINT fk_student_qualifications_verified_by FOREIGN KEY (verified_by) REFERENCES dbo.users(id),
            CONSTRAINT fk_student_qualifications_certificate_file FOREIGN KEY (certificate_file_id) REFERENCES dbo.files(id)
        );

        CREATE INDEX ix_student_qualifications_org_status_updated
            ON dbo.student_qualifications(organization_id, verification_status, updated_at DESC);
    END;

    IF OBJECT_ID(N'dbo.project_period_qualification_policies', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.project_period_qualification_policies
        (
            project_period_id BIGINT NOT NULL,
            require_student_qualification BIT NOT NULL CONSTRAINT df_ppqp_require DEFAULT 0,
            qualification_type VARCHAR(50) NOT NULL CONSTRAINT df_ppqp_type DEFAULT 'CAPSTONE_READINESS',
            require_certificate BIT NOT NULL CONSTRAINT df_ppqp_certificate DEFAULT 1,
            check_expiration BIT NOT NULL CONSTRAINT df_ppqp_expiration DEFAULT 1,
            updated_at DATETIME2(0) NOT NULL CONSTRAINT df_ppqp_updated DEFAULT SYSUTCDATETIME(),
            CONSTRAINT pk_project_period_qualification_policies PRIMARY KEY (project_period_id),
            CONSTRAINT fk_project_period_qualification_policy_period
                FOREIGN KEY (project_period_id) REFERENCES dbo.project_periods(id) ON DELETE CASCADE
        );
    END;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
