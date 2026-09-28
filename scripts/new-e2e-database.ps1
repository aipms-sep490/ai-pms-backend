[CmdletBinding()]
param(
    [string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION,
    [string]$DatabaseName = ('AI_PMS_E2E_' + [Guid]::NewGuid().ToString('N')),
    [switch]$VerifyRerun,
    [switch]$Drop
)
$ErrorActionPreference = 'Stop'
if ($DatabaseName -cnotmatch '^AI_PMS_E2E_[a-f0-9]{32}$') { throw 'Only owned AI_PMS_E2E_<guid> databases are allowed.' }
if ([string]::IsNullOrWhiteSpace($ConnectionString)) { throw 'Set AIPMS_TEST_SQL_CONNECTION through the process environment.' }
$root = Split-Path -Parent $PSScriptRoot
$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
$builder['Initial Catalog'] = 'master'
$builder['Pooling'] = $false
$builder['MultipleActiveResultSets'] = $false
function Execute($connection, [string]$sql) {
    $cmd = $connection.CreateCommand()
    try { $cmd.CommandText = $sql; $cmd.CommandTimeout = 120; [void]$cmd.ExecuteNonQuery() }
    finally { $cmd.Dispose() }
}
$master = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$created = $false
try {
    $master.Open()
    $cmd = $master.CreateCommand()
    $cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=N'$DatabaseName'"
    $exists = [int]$cmd.ExecuteScalar() -eq 1
    $cmd.Dispose()
    if ($exists -and -not ($VerifyRerun -or $Drop)) { throw 'Database exists; specify -VerifyRerun to replay or -Drop for cleanup.' }
    if (-not $exists) {
        if ($Drop) { return }
        if ($VerifyRerun) { throw 'Rerun requires an existing owned database.' }
        Execute $master "CREATE DATABASE [$DatabaseName]"
        $created = $true
    }
} finally { $master.Dispose() }
$builder['Initial Catalog'] = $DatabaseName
$target = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
try {
    $target.Open()
    if ($created) {
        Execute $target "EXEC sys.sp_addextendedproperty @name=N'AIPMS_E2E_OWNER', @value=N'remediation-v1';"
    }
    $cmd = $target.CreateCommand()
    $cmd.CommandText = "SELECT CAST(value AS nvarchar(100)) FROM sys.extended_properties WHERE class=0 AND name=N'AIPMS_E2E_OWNER'"
    $owner = $cmd.ExecuteScalar(); $cmd.Dispose()
    if ($owner -ne 'remediation-v1') { throw 'Database ownership marker missing; refusing mutation.' }
    if ($Drop) {
        $target.Close()
        $builder['Initial Catalog'] = 'master'
        $cleanup = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
        try { $cleanup.Open(); Execute $cleanup "ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$DatabaseName]" }
        finally { $cleanup.Dispose() }
        return
    }
    Execute $target @'
SET XACT_ABORT ON;
IF OBJECT_ID('dbo.e2e_script_ledger','U') IS NULL
CREATE TABLE dbo.e2e_script_ledger (
    script_name nvarchar(255) NOT NULL PRIMARY KEY, sha256 char(64) NOT NULL,
    applied_at datetime2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    applied_by nvarchar(128) NOT NULL DEFAULT ORIGINAL_LOGIN()
);
'@
    $manifest = Get-Content (Join-Path $root 'db/e2e/migrations.json') -Raw | ConvertFrom-Json
    $scripts = @('db/schema.sql') + @($manifest | ForEach-Object { "db/changes/$_" }) + @('db/seed.sql', 'db/e2e/seed.sql')
    # Identity V3, generated per run. Never persist or print plaintext credentials.
    $password = $env:AIPMS_E2E_PASSWORD
    if ([string]::IsNullOrWhiteSpace($password)) { $password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N') }
    $salt = [byte[]]::new(16); $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($salt) } finally { $rng.Dispose() }
    $derive = [Security.Cryptography.Rfc2898DeriveBytes]::new($password, $salt, 100000, [Security.Cryptography.HashAlgorithmName]::SHA512)
    try { $hash = [Convert]::ToBase64String([byte[]](@(1,0,0,0,2,0,1,134,160,0,0,0,16) + $salt + $derive.GetBytes(32))) }
    finally { $derive.Dispose(); $password = $null }
    $cmd = $target.CreateCommand()
    $cmd.CommandText = "EXEC sys.sp_set_session_context @key=N'e2e_password_hash', @value=@hash"
    [void]$cmd.Parameters.AddWithValue('@hash', $hash)
    [void]$cmd.ExecuteNonQuery(); $cmd.Dispose()
    foreach ($name in $scripts) {
        if ($name -notmatch '^db/(schema\.sql|seed\.sql|e2e/seed\.sql|changes/[a-zA-Z0-9_]+\.sql)$') { throw 'Invalid script path.' }
        $path = Join-Path $root $name
        $sql = [IO.File]::ReadAllText($path).Replace("`r`n", "`n")
        $hasher = [Security.Cryptography.SHA256]::Create()
        try { $checksum = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($sql))).Replace('-','').ToLowerInvariant() }
        finally { $hasher.Dispose() }
        $cmd = $target.CreateCommand()
        $cmd.CommandText = 'SELECT sha256 FROM dbo.e2e_script_ledger WHERE script_name=@name'
        [void]$cmd.Parameters.AddWithValue('@name', $name)
        $previous = $cmd.ExecuteScalar(); $cmd.Dispose()
        if ($previous -and $previous -ne $checksum) { throw "Script checksum changed: $name. Create a new E2E database or an additive migration." }
        if ($name -eq 'db/schema.sql') {
            if ($previous) { continue }
            $start = $sql.IndexOf('SET ANSI_NULLS ON;')
            if ($start -lt 0) { throw 'Unexpected schema format.' }
            $sql = $sql.Substring($start)
        }
        if ($name -in @('db/seed.sql','db/changes/20260825_add_account_security.sql','db/changes/20260825_add_project_metadata_tags.sql')) { $sql = $sql -replace '(?im)^USE \[AI_PMS\];[^\r\n]*$', '' }
        if ($sql -match '(?im)^\s*(USE\s|(?:CREATE|DROP|ALTER)\s+DATABASE\b)') { throw "Database switching is forbidden: $name" }
        foreach ($batch in [regex]::Split($sql, '(?im)^\s*GO\s*$')) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) { Execute $target $batch }
        }
        if (-not $previous) {
            $cmd = $target.CreateCommand()
            $cmd.CommandText = 'INSERT dbo.e2e_script_ledger(script_name,sha256) VALUES(@name,@hash)'
            [void]$cmd.Parameters.AddWithValue('@name', $name); [void]$cmd.Parameters.AddWithValue('@hash', $checksum)
            [void]$cmd.ExecuteNonQuery(); $cmd.Dispose()
        }
    }
    Execute $target ([IO.File]::ReadAllText((Join-Path $root 'db/e2e/verify.sql')))
    Write-Output $DatabaseName
} catch {
    Write-Warning "E2E database $DatabaseName retained for diagnosis. Use this script with -Drop for cleanup."
    throw
} finally { $target.Dispose() }
