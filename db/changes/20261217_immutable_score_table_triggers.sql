-- Wave 4 (TK-W4-07): Defense-in-depth immutability SQL triggers on the 4 finalized/published score tables.
-- Raises SQL error 51009 on any UPDATE or DELETE; INSERT-only operations remain supported (EF Core UseSqlOutputClause(false)).
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER TRIGGER dbo.tr_immutable_evaluation_finalizations
ON dbo.evaluation_finalizations
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51009, N'Immutable score table dbo.evaluation_finalizations cannot be updated or deleted.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.tr_immutable_project_results
ON dbo.project_results
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51009, N'Immutable score table dbo.project_results cannot be updated or deleted.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.tr_immutable_student_results
ON dbo.student_results
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51009, N'Immutable score table dbo.student_results cannot be updated or deleted.', 1;
END;
GO

CREATE OR ALTER TRIGGER dbo.tr_immutable_result_revisions
ON dbo.result_revisions
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51009, N'Immutable score table dbo.result_revisions cannot be updated or deleted.', 1;
END;
GO
