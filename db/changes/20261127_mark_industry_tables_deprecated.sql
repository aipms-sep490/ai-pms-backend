SET XACT_ABORT ON;
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- TK-W0-12: Mark 6 unreferenced industry_* tables as DEPRECATED without dropping data (additive, idempotent).
DECLARE @depTables TABLE (tbl sysname);
INSERT INTO @depTables(tbl)
VALUES (N'industry_experts'), (N'industry_invitations'), (N'industry_challenges'),
       (N'industry_assignments'), (N'industry_evidence_shares'), (N'industry_feedback');

DECLARE @tbl sysname;
DECLARE @desc nvarchar(500) = N'DEPRECATED: Out of scope for V4 core; do not reference in new code.';
DECLARE dep_cur CURSOR LOCAL FAST_FORWARD FOR SELECT tbl FROM @depTables;
OPEN dep_cur;
FETCH NEXT FROM dep_cur INTO @tbl;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @tbl, N'U') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1 FROM sys.extended_properties
            WHERE major_id = OBJECT_ID(N'dbo.' + @tbl) AND minor_id = 0 AND name = N'MS_Description'
        )
            EXEC sys.sp_updateextendedproperty @name = N'MS_Description', @value = @desc,
                @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @tbl;
        ELSE
            EXEC sys.sp_addextendedproperty @name = N'MS_Description', @value = @desc,
                @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = @tbl;
    END;
    FETCH NEXT FROM dep_cur INTO @tbl;
END;
CLOSE dep_cur;
DEALLOCATE dep_cur;

COMMIT TRANSACTION;
GO
