param([Parameter(Mandatory)][string]$Repository,[Parameter(Mandatory)][string]$PrivateSettings)
$ErrorActionPreference='Stop'
try {
    $settings=Get-Content -LiteralPath $PrivateSettings -Raw | ConvertFrom-Json
    $neutral=[Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString($settings.ConnectionStrings.DefaultConnection)
    $null=$neutral.Remove('Command Timeout')
    $sql=[Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
    $sql.set_DataSource('127.0.0.1,1433')
    if($sql.get_InitialCatalog() -cne 'AI_PMS'){throw 'Unexpected database.'}
    $env:AIPMS_TEST_SQL_CONNECTION=$sql.get_ConnectionString()
    $env:AIPMS_SCHEMA_CHECK_CONNECTION=$sql.get_ConnectionString()
    & pwsh -NoProfile -File (Join-Path $Repository 'scripts/test-schema-readiness.ps1')
    if($LASTEXITCODE -ne 0){throw 'General schema readiness failed.'}
    & pwsh -NoProfile -File (Join-Path $Repository 'scripts/verify-academic-schema-readiness.ps1')
    if($LASTEXITCODE -ne 0){throw 'Academic schema readiness failed.'}
} catch {
    Write-Output 'Schema verification failed; private settings and connection values are not printed.'
    exit 1
} finally {
    Remove-Item Env:AIPMS_TEST_SQL_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:AIPMS_SCHEMA_CHECK_CONNECTION -ErrorAction SilentlyContinue
    $settings=$null;$sql=$null;$neutral=$null
}
