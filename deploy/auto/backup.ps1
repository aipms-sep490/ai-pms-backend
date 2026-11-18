param([Parameter(Mandatory)][string]$PrivateSettings,[Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
try {
    $settings=Get-Content -LiteralPath $PrivateSettings -Raw | ConvertFrom-Json
    $neutral=[Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString($settings.ConnectionStrings.DefaultConnection)
    $null=$neutral.Remove('Command Timeout')
    $sql=[Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
    $sql.set_DataSource('127.0.0.1,1433')
    if($sql.get_InitialCatalog() -cne 'AI_PMS'){throw 'Unexpected database.'}
    $connection=[Data.SqlClient.SqlConnection]::new($sql.get_ConnectionString())
    $serverPath='/var/opt/mssql/backup/autodeploy_'+[Guid]::NewGuid().ToString('N')+'.bak'
    $connection.Open();$command=$connection.CreateCommand();$command.CommandTimeout=600
    $command.CommandText='BACKUP DATABASE [AI_PMS] TO DISK=@path WITH COPY_ONLY,CHECKSUM,INIT'
    $null=$command.Parameters.AddWithValue('@path',$serverPath);$null=$command.ExecuteNonQuery()
    $command.CommandText='RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM';$null=$command.ExecuteNonQuery()
    $destination=Join-Path $ReleaseDirectory 'database.before.private.bak'
    if(Test-Path -LiteralPath $destination){throw 'Backup destination already exists.'}
    & docker cp ('mssql:'+$serverPath) $destination
    if($LASTEXITCODE -ne 0){throw 'Backup copy failed.'}
    & chmod 600 $destination
    if($LASTEXITCODE -ne 0){throw 'Backup permissions failed.'}
    $localHash=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    $remoteHash= & docker exec mssql sha256sum $serverPath
    if($LASTEXITCODE -ne 0 -or ($remoteHash -split '\s+')[0] -cne $localHash){throw 'Backup checksum mismatch.'}
    Write-Output 'COPY_ONLY backup, CHECKSUM, RESTORE VERIFYONLY and copied SHA-256 passed.'
} catch {
    Write-Output 'Database backup failed; existing application retained. Connection values are not printed.'
    exit 1
} finally {
    if($command){$command.Dispose()};if($connection){$connection.Dispose()}
    $settings=$null;$sql=$null;$neutral=$null
}
