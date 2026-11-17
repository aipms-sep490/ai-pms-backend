[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedServer,
    [Parameter(Mandatory)][string]$ExpectedDatabase,
    [string]$ConnectionString = $env:AIPMS_MIGRATION_CONNECTION
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Set AIPMS_MIGRATION_CONNECTION in the process environment.' }
if ($ExpectedDatabase -cnotmatch '^[A-Za-z][A-Za-z0-9_]{0,127}$') { throw 'Invalid expected database name.' }

$root = Split-Path -Parent $PSScriptRoot
$scripts = @(
    '20261030_add_rubric_criterion_levels.sql',
    '20261030_add_rubric_scope_fields.sql',
    '20261113_prepare_defense_committee_columns.sql',
    '20261114_confirm_defense_committees.sql',
    '20261114_cold_common_checklist.sql',
    '20261115_department_document_templates.sql',
    '20261117_user_avatars.sql'
)

$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
$builder['Pooling'] = $false
$builder['MultipleActiveResultSets'] = $false
$builder['ApplicationIntent'] = 'ReadWrite'
$connection = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
$ConnectionString = $null
$backupVerified = $false

function Invoke-CommandText([System.Data.SqlClient.SqlConnection]$Target, [string]$Sql, [int]$Timeout = 120) {
    $command = $Target.CreateCommand()
    try {
        $command.CommandText = $Sql
        $command.CommandTimeout = $Timeout
        [void]$command.ExecuteNonQuery()
    } finally { $command.Dispose() }
}

try {
    $connection.Open()
    $identity = $connection.CreateCommand()
    try {
        $identity.CommandText = "SELECT DB_NAME(), CAST(SERVERPROPERTY('ServerName') AS nvarchar(128)), CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(260)), HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE','BACKUP DATABASE'), CAST(SERVERPROPERTY('EngineEdition') AS int)"
        $reader = $identity.ExecuteReader()
        try {
            [void]$reader.Read()
            $actualDatabase = $reader.GetString(0)
            $actualServer = $reader.GetString(1)
            $backupDirectory = if ($reader.IsDBNull(2)) { '' } else { $reader.GetString(2) }
            $canBackup = $reader.GetInt32(3)
            $engineEdition = $reader.GetInt32(4)
        } finally { $reader.Dispose() }
    } finally { $identity.Dispose() }

    if ($actualDatabase -cne $ExpectedDatabase -or $actualServer -cne $ExpectedServer) {
        throw 'Connected server/database does not match the explicit target. No changes made.'
    }
    if ($actualDatabase -in @('master', 'model', 'msdb', 'tempdb') -or $canBackup -ne 1 -or [string]::IsNullOrWhiteSpace($backupDirectory)) {
        throw 'Target is a system database or a verified backup cannot be prepared. No changes made.'
    }

    # A unique copy-only backup does not alter the regular differential backup base.
    $separator = if ($backupDirectory.Contains('\')) { '\' } else { '/' }
    $backupPath = $backupDirectory.TrimEnd('\', '/') + $separator +
        $ExpectedDatabase + '_pre_academic_' + [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss') + '_' +
        [Guid]::NewGuid().ToString('N') + '.bak'
    $backup = $connection.CreateCommand()
    try {
        $compression = if ($engineEdition -eq 4) { '' } else { ', COMPRESSION' }
        $backup.CommandText = "BACKUP DATABASE [$ExpectedDatabase] TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT$compression"
        $backup.CommandTimeout = 3600
        [void]$backup.Parameters.AddWithValue('@path', $backupPath)
        [void]$backup.ExecuteNonQuery()
        $backup.CommandText = 'RESTORE VERIFYONLY FROM DISK = @path WITH CHECKSUM'
        [void]$backup.ExecuteNonQuery()
        $backupVerified = $true
    } finally { $backup.Dispose() }
    Write-Output "BACKUP_VERIFIED $backupPath"

    Invoke-CommandText $connection @'
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.academic_schema_migration_ledger', N'U') IS NULL
    CREATE TABLE dbo.academic_schema_migration_ledger (
        script_name nvarchar(255) NOT NULL PRIMARY KEY,
        sha256 char(64) NOT NULL,
        applied_at datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
        applied_by nvarchar(128) NOT NULL DEFAULT ORIGINAL_LOGIN()
    );
'@

    foreach ($name in $scripts) {
        $path = Join-Path $root "db/changes/$name"
        $sql = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        if ($sql -match '(?im)^\s*(USE\s|(?:CREATE|DROP|ALTER)\s+DATABASE\b)') {
            throw "Database switching is forbidden: $name"
        }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sql))).ToLowerInvariant()
        $lookup = $connection.CreateCommand()
        try {
            $lookup.CommandText = 'SELECT sha256 FROM dbo.academic_schema_migration_ledger WHERE script_name=@name'
            [void]$lookup.Parameters.AddWithValue('@name', $name)
            $previous = $lookup.ExecuteScalar()
        } finally { $lookup.Dispose() }
        if ($previous -and $previous -cne $hash) { throw "Migration checksum changed: $name" }
        if ($previous) { Write-Output "ALREADY_APPLIED $name"; continue }

        foreach ($batch in [regex]::Split($sql, '(?im)^\s*GO\s*$')) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) { Invoke-CommandText $connection $batch }
        }
        $record = $connection.CreateCommand()
        try {
            $record.CommandText = 'INSERT dbo.academic_schema_migration_ledger(script_name,sha256) VALUES(@name,@hash)'
            [void]$record.Parameters.AddWithValue('@name', $name)
            [void]$record.Parameters.AddWithValue('@hash', $hash)
            [void]$record.ExecuteNonQuery()
        } finally { $record.Dispose() }
        Write-Output "APPLIED $name"
    }
} catch {
    if ($backupVerified) {
        Write-Warning 'Academic release stopped. Preserve the verified backup and inspect the failing migration before retrying; no automatic database restore is attempted.'
    } else {
        Write-Warning 'Academic release stopped before a backup was verified. No migration was attempted.'
    }
    throw
} finally {
    $connection.Dispose()
}
