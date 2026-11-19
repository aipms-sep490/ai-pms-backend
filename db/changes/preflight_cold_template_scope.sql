-- ==============================================================================================
-- PREFLIGHT AUDIT: Cold Report Template Common Scope Schema Verification
-- File: db/changes/preflight_cold_template_scope.sql
-- Description: Read-only diagnostic script executed BEFORE applying 20261030_cold_template_common_scope.sql
-- Safety: READ-ONLY. No DDL, no DML, no transactions, no state mutation.
-- Target: AI-PMS SQL Server Staging Database
-- ==============================================================================================

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

PRINT '================================================================================';
PRINT ' AI-PMS STAGING MIGRATION PREFLIGHT AUDIT';
PRINT ' Target Script: db/changes/20261030_cold_template_common_scope.sql';
PRINT ' Execution Timestamp: ' + CONVERT(varchar(30), SYSUTCDATETIME(), 126) + ' UTC';
PRINT '================================================================================';
PRINT '';

-- 1. Server, Database & Identity Context
SELECT 
    CAST(SERVERPROPERTY('ServerName') AS NVARCHAR(128)) AS [CurrentServer],
    DB_NAME() AS [CurrentDatabase],
    ORIGINAL_LOGIN() AS [OriginalLogin],
    CURRENT_USER AS [CurrentUser],
    HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') AS [HasDatabaseAlterPermission],
    HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'BACKUP DATABASE') AS [HasBackupPermission],
    CAST(SERVERPROPERTY('EngineEdition') AS INT) AS [EngineEdition],
    CAST(SERVERPROPERTY('ProductVersion') AS NVARCHAR(128)) AS [ProductVersion];

-- 2. Verify Table Existence
IF OBJECT_ID(N'dbo.cold_report_templates', N'U') IS NULL
BEGIN
    PRINT '>> ERROR: Table dbo.cold_report_templates does NOT exist in this database.';
    PRINT '>> Status: BLOCKED - Prerequisites missing.';
END
ELSE
BEGIN
    PRINT '>> OK: Table dbo.cold_report_templates exists.';
END

-- 3. Detailed Column Specification Check
SELECT 
    c.name AS [ColumnName],
    t.name AS [DataType],
    c.max_length AS [MaxLengthBytes],
    c.is_nullable AS [IsNullable],
    c.column_id AS [ColumnId]
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.cold_report_templates')
  AND c.name IN (N'id', N'major_id', N'scope', N'project_period_id', N'version', N'name')
ORDER BY c.column_id;

-- 4. Check Constraints Status
SELECT 
    cc.name AS [ConstraintName],
    cc.definition AS [CheckClause],
    cc.is_disabled AS [IsDisabled],
    cc.is_not_trusted AS [IsNotTrusted]
FROM sys.check_constraints cc
WHERE cc.parent_object_id = OBJECT_ID(N'dbo.cold_report_templates')
  AND cc.name IN (N'ck_cold_template_scope', N'ck_cold_template_scope_major');

-- 5. Existing Data Compatibility & Potential Conflict Check
IF OBJECT_ID(N'dbo.cold_report_templates', N'U') IS NOT NULL
BEGIN
    DECLARE @TotalRows BIGINT = 0;
    SELECT @TotalRows = COUNT_BIG(*) FROM dbo.cold_report_templates;
    
    PRINT '>> Table dbo.cold_report_templates row count: ' + CAST(@TotalRows AS VARCHAR(20));

    -- If scope column exists, check existing scope distributions
    IF COL_LENGTH(N'dbo.cold_report_templates', N'scope') IS NOT NULL
    BEGIN
        EXEC sp_executesql N'
            SELECT 
                ISNULL(scope, N''<NULL>'') AS [ScopeValue],
                COUNT(*) AS [RowCount],
                SUM(CASE WHEN major_id IS NULL THEN 1 ELSE 0 END) AS [NullMajorCount],
                SUM(CASE WHEN major_id IS NOT NULL THEN 1 ELSE 0 END) AS [NonNullMajorCount]
            FROM dbo.cold_report_templates
            GROUP BY scope;
        ';

        -- Check for any rows that would violate ck_cold_template_scope_major
        DECLARE @ConflictingScopeMajorCount INT = 0;
        EXEC sp_executesql N'
            SELECT @ConflictingScopeMajorCount = COUNT(*) 
            FROM dbo.cold_report_templates 
            WHERE scope = ''COMMON'' AND major_id IS NOT NULL;',
            N'@ConflictingScopeMajorCount INT OUTPUT',
            @ConflictingScopeMajorCount OUTPUT;

        IF @ConflictingScopeMajorCount > 0
            PRINT '>> WARNING: Found ' + CAST(@ConflictingScopeMajorCount AS VARCHAR(10)) + ' rows with scope=COMMON and non-null major_id!';
        ELSE
            PRINT '>> OK: No data conflicts with ck_cold_template_scope_major.';

        -- Check for any rows with invalid scope string
        DECLARE @InvalidScopeCount INT = 0;
        EXEC sp_executesql N'
            SELECT @InvalidScopeCount = COUNT(*) 
            FROM dbo.cold_report_templates 
            WHERE scope IS NOT NULL AND scope NOT IN (''COMMON'', ''MAJOR_SPECIFIC'', ''INDIVIDUAL'');',
            N'@InvalidScopeCount INT OUTPUT',
            @InvalidScopeCount OUTPUT;

        IF @InvalidScopeCount > 0
            PRINT '>> WARNING: Found ' + CAST(@InvalidScopeCount AS VARCHAR(10)) + ' rows with invalid scope strings!';
        ELSE
            PRINT '>> OK: No invalid scope values detected.';
    END
    ELSE
    BEGIN
        PRINT '>> Column [scope] does not yet exist. Existing templates will have scope = NULL upon addition (safe & non-breaking).';
    END
END

-- 6. Migration Ledger Audit
IF OBJECT_ID(N'dbo.academic_schema_migration_ledger', N'U') IS NOT NULL
BEGIN
    SELECT 
        script_name AS [ScriptName],
        sha256 AS [Checksum],
        applied_at AS [AppliedAtUtc],
        applied_by AS [AppliedBy]
    FROM dbo.academic_schema_migration_ledger
    WHERE script_name = N'20261030_cold_template_common_scope.sql';
END
ELSE
BEGIN
    PRINT '>> Note: dbo.academic_schema_migration_ledger table does not exist yet (will be created automatically if run via runner).';
END

-- 7. Diagnostic Summary & Verdict
DECLARE @ScopeExists BIT = CASE WHEN COL_LENGTH(N'dbo.cold_report_templates', N'scope') IS NOT NULL THEN 1 ELSE 0 END;
DECLARE @MajorIsNullable BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.cold_report_templates') AND name = N'major_id' AND is_nullable = 1) THEN 1 ELSE 0 END;
DECLARE @Constraint1Exists BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates') AND name = N'ck_cold_template_scope') THEN 1 ELSE 0 END;
DECLARE @Constraint2Exists BIT = CASE WHEN EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.cold_report_templates') AND name = N'ck_cold_template_scope_major') THEN 1 ELSE 0 END;

PRINT '';
PRINT '--------------------------------------------------------------------------------';
PRINT ' PREFLIGHT SUMMARY:';
PRINT ' - Column [scope] exists:                   ' + CASE @ScopeExists WHEN 1 THEN 'YES (Already Present)' ELSE 'NO (Needs Migration)' END;
PRINT ' - Column [major_id] is nullable:           ' + CASE @MajorIsNullable WHEN 1 THEN 'YES (Compatible)' ELSE 'NO (Needs Alter)' END;
PRINT ' - Constraint ck_cold_template_scope:       ' + CASE @Constraint1Exists WHEN 1 THEN 'YES' ELSE 'NO (Needs Migration)' END;
PRINT ' - Constraint ck_cold_template_scope_major: ' + CASE @Constraint2Exists WHEN 1 THEN 'YES' ELSE 'NO (Needs Migration)' END;
PRINT '--------------------------------------------------------------------------------';

IF @ScopeExists = 1 AND @MajorIsNullable = 1 AND @Constraint1Exists = 1 AND @Constraint2Exists = 1
    PRINT '>> VERDICT: ALREADY_APPLIED — The database schema is already complete. Migration is idempotent and can be safely re-run without changes.';
ELSE
    PRINT '>> VERDICT: READY_TO_APPLY — Schema changes are required. Run db/changes/20261030_cold_template_common_scope.sql or the migration runner.';
PRINT '================================================================================';
