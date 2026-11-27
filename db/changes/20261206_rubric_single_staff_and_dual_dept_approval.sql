SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF COL_LENGTH('dbo.rubrics', 'participating_department_id') IS NULL
BEGIN
    ALTER TABLE dbo.rubrics
        ADD participating_department_id BIGINT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_rubrics_participating_department' AND parent_object_id = OBJECT_ID(N'dbo.rubrics'))
BEGIN
    ALTER TABLE dbo.rubrics
        ADD CONSTRAINT fk_rubrics_participating_department
            FOREIGN KEY (participating_department_id) REFERENCES dbo.departments(id)
            ON DELETE NO ACTION ON UPDATE NO ACTION;
END
GO

IF COL_LENGTH('dbo.rubric_approvals', 'department_id') IS NULL
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD department_id BIGINT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'fk_rubric_approvals_department' AND parent_object_id = OBJECT_ID(N'dbo.rubric_approvals'))
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD CONSTRAINT fk_rubric_approvals_department
            FOREIGN KEY (department_id) REFERENCES dbo.departments(id)
            ON DELETE NO ACTION ON UPDATE NO ACTION;
END
GO

IF COL_LENGTH('dbo.rubric_approvals', 'scope_role') IS NULL
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD scope_role VARCHAR(20) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_rubric_approvals_scope_role' AND parent_object_id = OBJECT_ID(N'dbo.rubric_approvals'))
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD CONSTRAINT ck_rubric_approvals_scope_role
            CHECK (scope_role IS NULL OR scope_role IN ('LEAD', 'PARTICIPATING'));
END
GO

IF COL_LENGTH('dbo.rubric_approvals', 'is_single_staff_self_approval') IS NULL
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD is_single_staff_self_approval BIT NOT NULL
            CONSTRAINT df_rubric_approvals_single_staff DEFAULT (0);
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_rubric_approvals_four_eyes' AND parent_object_id = OBJECT_ID(N'dbo.rubric_approvals'))
BEGIN
    ALTER TABLE dbo.rubric_approvals DROP CONSTRAINT ck_rubric_approvals_four_eyes;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'ck_rubric_approvals_four_eyes' AND parent_object_id = OBJECT_ID(N'dbo.rubric_approvals'))
BEGIN
    ALTER TABLE dbo.rubric_approvals
        ADD CONSTRAINT ck_rubric_approvals_four_eyes
            CHECK (decided_by IS NULL OR decided_by <> submitted_by OR is_single_staff_self_approval = 1);
END
GO
