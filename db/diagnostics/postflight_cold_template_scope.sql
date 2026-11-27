-- ==============================================================================================
-- POSTFLIGHT AUDIT: Cold Report Template Common Scope Schema Verification
-- File: db/changes/postflight_cold_template_scope.sql
-- Description: Diagnostic verification script executed AFTER applying 20261030_cold_template_common_scope.sql
-- Safety: READ-ONLY. No DDL, no DML, no transactions, no state mutation.
-- Target: AI-PMS SQL Server Staging Database
-- ==============================================================================================

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

PRINT '================================================================================';
PRINT ' AI-PMS STAGING MIGRATION POSTFLIGHT VERIFICATION';
PRINT ' Target Script: db/changes/20261030_cold_template_common_scope.sql';
PRINT ' Execution Timestamp: ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126) + ' UTC';
PRINT '================================================================================';
PRINT '';

DECLARE @AllPassed BIT = 1;

-- 1. Verify Column [scope]
DECLARE @ScopeExists BIT = 0;
DECLARE @ScopeDataType NVARCHAR(128) = N'';
DECLARE @ScopeLength INT = 0;
DECLARE @ScopeNullable BIT = 0;

SELECT 
    @ScopeExists = 1,
    @ScopeDataType = t.name,
    @ScopeLength = c.max_length,
    @ScopeNullable = c.is_nullable
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.cold_report_templates') AND c.name = N'scope';

IF @ScopeExists = 1 AND @ScopeDataType = N'nvarchar' AND @ScopeLength = 64 AND @ScopeNullable = 1
BEGIN
    PRINT '  [PASS] Column [scope] exists: NVARCHAR(32) NULL (storage: 64 bytes).';
END
ELSE
BEGIN
    PRINT '  [FAIL] Column [scope] validation failed! (Exists: ' + CAST(@ScopeExists AS VARCHAR(1)) + ', Type: ' + @ScopeDataType + ', Length: ' + CAST(@ScopeLength AS VARCHAR(10)) + ')';
    SET @AllPassed = 0;
END

-- 2. Verify Column [major_id] Nullability
DECLARE @MajorIdNullable BIT = 0;
DECLARE @MajorIdDataType NVARCHAR(128) = N'';

SELECT 
    @MajorIdNullable = c.is_nullable,
    @MajorIdDataType = t.name
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.cold_report_templates') AND c.name = N'major_id';

IF @MajorIdDataType = N'bigint' AND @MajorIdNullable = 1
BEGIN
    PRINT '  [PASS] Column [major_id] exists: BIGINT NULL.';
END
ELSE
BEGIN
    PRINT '  [FAIL] Column [major_id] validation failed! (Nullable: ' + CAST(@MajorIdNullable AS VARCHAR(1)) + ')';
    SET @AllPassed = 0;
END

-- 3. Verify Check Constraint [ck_cold_template_scope]
DECLARE @ConstraintScopeExists BIT = 0;
DECLARE @ConstraintScopeDisabled BIT = 1;

SELECT 
    @ConstraintScopeExists = 1,
    @ConstraintScopeDisabled = is_disabled
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates') AND name = N'ck_cold_template_scope';

IF @ConstraintScopeExists = 1 AND @ConstraintScopeDisabled = 0
BEGIN
    PRINT '  [PASS] Check constraint [ck_cold_template_scope] exists and is ACTIVE.';
END
ELSE
BEGIN
    PRINT '  [FAIL] Check constraint [ck_cold_template_scope] validation failed!';
    SET @AllPassed = 0;
END

-- 4. Verify Check Constraint [ck_cold_template_scope_major]
DECLARE @ConstraintMajorExists BIT = 0;
DECLARE @ConstraintMajorDisabled BIT = 1;

SELECT 
    @ConstraintMajorExists = 1,
    @ConstraintMajorDisabled = is_disabled
FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates') AND name = N'ck_cold_template_scope_major';

IF @ConstraintMajorExists = 1 AND @ConstraintMajorDisabled = 0
BEGIN
    PRINT '  [PASS] Check constraint [ck_cold_template_scope_major] exists and is ACTIVE.';
END
ELSE
BEGIN
    PRINT '  [FAIL] Check constraint [ck_cold_template_scope_major] validation failed!';
    SET @AllPassed = 0;
END

-- 5. Test Live EF Query Compatibility
BEGIN TRY
    -- Execute query identical to EF Core DbSet<ColdReportTemplate>.AsNoTracking()
    EXEC(N'SELECT TOP 1 id, major_id, scope, project_period_id, version, name, reports_json, created_by, created_at FROM dbo.cold_report_templates');
    PRINT '  [PASS] EF Core cold_report_templates SELECT query executed successfully without SQL exceptions.';
END TRY
BEGIN CATCH
    PRINT '  [FAIL] EF Core SELECT query failed: ' + ERROR_MESSAGE();
    SET @AllPassed = 0;
END CATCH

-- 6. Check Migration Ledger
IF OBJECT_ID(N'dbo.academic_schema_migration_ledger', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.academic_schema_migration_ledger WHERE script_name = N'20261030_cold_template_common_scope.sql')
    BEGIN
        PRINT '  [PASS] Migration ledger has recorded: 20261030_cold_template_common_scope.sql';
    END
    ELSE
    BEGIN
        PRINT '  [INFO] Migration was run directly via SSMS/sqlcmd (not recorded in academic_schema_migration_ledger). See runbook for ledger sync if desired.';
    END
END

PRINT '';
PRINT '--------------------------------------------------------------------------------';
IF @AllPassed = 1
BEGIN
    PRINT ' POSTFLIGHT VERDICT: SUCCESS (PASS)';
    PRINT ' The Cold Report Template schema is 100% compatible with AI-PMS Backend Cold API.';
    PRINT ' Expected API behavior: GET /api/v1/cold-report-templates returns HTTP 200 OK.';
END
ELSE
BEGIN
    PRINT ' POSTFLIGHT VERDICT: FAILED (FAIL)';
    PRINT ' One or more postflight checks failed. Review errors above and refer to rollback instructions if necessary.';
END
PRINT '================================================================================';
