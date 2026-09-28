[CmdletBinding()]
param([string]$ConnectionString = $env:AIPMS_TEST_SQL_CONNECTION)
$ErrorActionPreference = 'Stop'
$name = 'AI_PMS_E2E_' + [Guid]::NewGuid().ToString('N')
$script = Join-Path $PSScriptRoot 'new-e2e-database.ps1'
$neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
$neutral.set_ConnectionString($ConnectionString)
[void]$neutral.Remove('Command Timeout')
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
$builder['Initial Catalog'] = $name
$builder['Pooling'] = $false
function Snapshot {
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = @'
SELECT CONCAT((SELECT COUNT(*) FROM dbo.users),':',(SELECT COUNT(*) FROM dbo.projects),':',
    (SELECT COUNT(*) FROM dbo.team_members),':',(SELECT COUNT(*) FROM dbo.e2e_script_ledger),':',
    (SELECT STRING_AGG(CONCAT(alias_name,'=',entity_id),';') WITHIN GROUP (ORDER BY alias_name) FROM dbo.e2e_aliases),':',
    (SELECT password_hash FROM dbo.users WHERE email=N'leader@e2e.invalid'));
'@
        try { return $command.ExecuteScalar() } finally { $command.Dispose() }
    } finally { $connection.Dispose() }
}
try {
    & $script -ConnectionString $ConnectionString -DatabaseName $name | Out-Null
    $before = Snapshot
    & (Join-Path $PSScriptRoot 'test-schema-readiness.ps1') -ConnectionString $builder.ConnectionString | Out-Null
    & $script -ConnectionString $ConnectionString -DatabaseName $name -VerifyRerun | Out-Null
    if ($before -cne (Snapshot)) { throw 'Rerun changed seeded identities, counts or password hashes.' }
    foreach ($invalid in @('AI_PMS','master','AI_PMS_E2E_not-a-guid')) {
        $refused = $false
        try { & $script -ConnectionString $ConnectionString -DatabaseName $invalid -Drop | Out-Null }
        catch { $refused = $true }
        if (-not $refused) { throw 'Unsafe database name was accepted.' }
    }
    # Simulate tampered release history; no application table is modified.
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open(); $command = $connection.CreateCommand()
        $command.CommandText = "UPDATE dbo.e2e_script_ledger SET sha256=REPLICATE('0',64) WHERE script_name='db/e2e/seed.sql'"
        [void]$command.ExecuteNonQuery(); $command.Dispose()
    } finally { $connection.Dispose() }
    $refused = $false
    try { & $script -ConnectionString $ConnectionString -DatabaseName $name -VerifyRerun | Out-Null }
    catch { if ($_.Exception.Message -like '*checksum changed*') { $refused = $true } else { throw } }
    if (-not $refused) { throw 'Changed checksum was accepted.' }
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open(); $command = $connection.CreateCommand()
        $command.CommandText = "EXEC sys.sp_dropextendedproperty @name=N'AIPMS_E2E_OWNER'"
        [void]$command.ExecuteNonQuery()
        try {
            $refused = $false
            try { & $script -ConnectionString $ConnectionString -DatabaseName $name -Drop | Out-Null }
            catch { if ($_.Exception.Message -like '*ownership marker missing*') { $refused = $true } else { throw } }
            if (-not $refused) { throw 'Database without owner marker was accepted.' }
        } finally {
            $command.CommandText = "EXEC sys.sp_addextendedproperty @name=N'AIPMS_E2E_OWNER', @value=N'remediation-v1'"
            [void]$command.ExecuteNonQuery(); $command.Dispose()
        }
    } finally { $connection.Dispose() }
    Write-Output 'PASS: create, schema readiness, reconnect, rerun, stable aliases/password hashes, unsafe-name and ownership refusal, checksum guard.'
} finally { & $script -ConnectionString $ConnectionString -DatabaseName $name -Drop }
