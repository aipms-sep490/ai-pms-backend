[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedInventory,
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ConnectionString = $env:AIPMS_AUDIT_SQL_CONNECTION
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Provide AIPMS_AUDIT_SQL_CONNECTION privately.' }
$root = Split-Path -Parent $PSScriptRoot
$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
$connection = [System.Data.SqlClient.SqlConnection]::new($neutral.get_ConnectionString())
function Rows([string]$sql) {
    $command = $connection.CreateCommand()
    $command.CommandText = $sql
    $command.CommandTimeout = 60
    try {
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) } }
                [pscustomobject]$row
            }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
}
function Quote([string]$name) { '[' + $name.Replace(']', ']]') + ']' }
$started = [DateTime]::UtcNow
try {
    $connection.Open()
    $permissions = @(Rows "SELECT HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','VIEW DEFINITION') AS metadataVisible")
    if ($permissions[0].metadataVisible -ne 1) { throw 'VIEW DEFINITION is required; a partial inventory is not a passing audit.' }
    $actual = @(Rows (Get-Content (Join-Path $root 'db/e2e/schema-inventory.sql') -Raw) | ForEach-Object { $_.signature.Replace("`r`n", "`n").Trim() })
    $expected = @(Get-Content -LiteralPath $ExpectedInventory -Raw | ConvertFrom-Json)
    # Compare as multisets so an extra duplicate structural index is still detected.
    $expectedCounts = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    $actualCounts = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    foreach ($value in $expected) { if (!$expectedCounts.ContainsKey($value)) { $expectedCounts[$value] = 0 }; $expectedCounts[$value]++ }
    foreach ($value in $actual) { if (!$actualCounts.ContainsKey($value)) { $actualCounts[$value] = 0 }; $actualCounts[$value]++ }
    $missing = @(); $extra = @()
    foreach ($value in $expectedCounts.Keys) { $count = 0; [void]$actualCounts.TryGetValue($value, [ref]$count); if ($count -lt $expectedCounts[$value]) { $missing += @{ signature=$value; count=$expectedCounts[$value]-$count } } }
    foreach ($value in $actualCounts.Keys) { $count = 0; [void]$expectedCounts.TryGetValue($value, [ref]$count); if ($count -lt $actualCounts[$value]) { $extra += @{ signature=$value; count=$actualCounts[$value]-$count } } }
    $foreignKeys = @(Rows @'
SELECT f.object_id AS id,f.name,SCHEMA_NAME(p.schema_id) AS parentSchema,p.name AS parentTable,
 SCHEMA_NAME(r.schema_id) AS referencedSchema,r.name AS referencedTable,pc.name AS parentColumn,rc.name AS referencedColumn,
 fc.constraint_column_id AS ordinal
FROM sys.foreign_keys f JOIN sys.tables p ON p.object_id=f.parent_object_id JOIN sys.tables r ON r.object_id=f.referenced_object_id
JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
JOIN sys.columns pc ON pc.object_id=p.object_id AND pc.column_id=fc.parent_column_id
JOIN sys.columns rc ON rc.object_id=r.object_id AND rc.column_id=fc.referenced_column_id
WHERE p.is_ms_shipped=0 ORDER BY f.object_id,fc.constraint_column_id
'@)
    $integrity = @()
    foreach ($group in ($foreignKeys | Group-Object id)) {
        $fk = $group.Group[0]
        $parent = (Quote $fk.parentSchema)+'.'+(Quote $fk.parentTable)
        $referenced = (Quote $fk.referencedSchema)+'.'+(Quote $fk.referencedTable)
        $nonnull = ($group.Group | ForEach-Object { 'p.'+(Quote $_.parentColumn)+' IS NOT NULL' }) -join ' AND '
        $join = ($group.Group | ForEach-Object { 'p.'+(Quote $_.parentColumn)+'=r.'+(Quote $_.referencedColumn) }) -join ' AND '
        $count = @(Rows "SELECT COUNT_BIG(*) AS violations FROM $parent p WHERE $nonnull AND NOT EXISTS(SELECT 1 FROM $referenced r WHERE $join)")[0].violations
        $integrity += @{ kind='FOREIGN_KEY'; table=$parent; name=$fk.name; violations=$count }
    }
    $checks = @(Rows "SELECT c.name,SCHEMA_NAME(t.schema_id) AS schemaName,t.name AS tableName,c.definition FROM sys.check_constraints c JOIN sys.tables t ON t.object_id=c.parent_object_id WHERE t.is_ms_shipped=0")
    foreach ($check in $checks) {
        $table = (Quote $check.schemaName)+'.'+(Quote $check.tableName)
        $count = @(Rows "SELECT COUNT_BIG(*) AS violations FROM $table WHERE NOT ($($check.definition))")[0].violations
        $integrity += @{ kind='CHECK'; table=$table; name=$check.name; violations=$count }
    }
    $businessSignals = @(Rows (Get-Content (Join-Path $root 'db/e2e/v5-integrity-readonly.sql') -Raw))
    $report = [ordered]@{
        startedAtUtc=$started; completedAtUtc=[DateTime]::UtcNow; readOnly=$true
        consistency='Read committed observations; concurrent application writes are possible.'
        expectedInventorySha256=(Get-FileHash -LiteralPath $ExpectedInventory -Algorithm SHA256).Hash
        sourceCommit=(& git -C $root rev-parse HEAD); sourceDirty=[bool](& git -C $root status --porcelain)
        expectedSignatures=$expected.Count; actualSignatures=$actual.Count
        missing=$missing; extra=$extra; integrity=$integrity
        schemaMatch=($missing.Count -eq 0 -and $extra.Count -eq 0)
        integrityPass=(@($integrity | Where-Object { $_.violations -gt 0 }).Count -eq 0)
        businessSignals=$businessSignals
        businessProvenance='Not inferred or repaired. Requires separate immutable snapshot/lifecycle acceptance.'
    }
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    Write-Output "Audit saved: $OutputPath. Missing: $($missing.Count); extra: $($extra.Count); integrity checks: $($integrity.Count)."
} finally { $connection.Dispose() }
