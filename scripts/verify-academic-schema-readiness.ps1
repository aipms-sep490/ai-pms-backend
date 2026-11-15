[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$rawConnection = $env:AIPMS_SCHEMA_CHECK_CONNECTION
if ([string]::IsNullOrWhiteSpace($rawConnection)) {
    Write-Error 'Set AIPMS_SCHEMA_CHECK_CONNECTION in the process environment before running this read-only check.'
    exit 3
}

# This check reads metadata only. The release process applies reviewed migrations separately.
$requirements = @(
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.department_document_templates'; Column = ''; Migration = '20261115_department_document_templates.sql' }
    [pscustomobject]@{ Kind = 'Table'; Object = 'dbo.rubric_criterion_levels'; Column = ''; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubric_criteria'; Column = 'learning_outcome'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubric_criteria'; Column = 'evidence_guidance'; Migration = '20261030_add_rubric_criterion_levels.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'major_id'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'project_mode'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'evaluation_stage'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'evaluation_scope'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'effective_from'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'effective_to'; Migration = '20261030_add_rubric_scope_fields.sql' }
    [pscustomobject]@{ Kind = 'Column'; Object = 'dbo.rubrics'; Column = 'approval_reference'; Migration = '20261030_add_rubric_scope_fields.sql' }
)

try {
    $neutral = [System.Data.Common.DbConnectionStringBuilder]::new()
    $neutral.set_ConnectionString($rawConnection)
    [void]$neutral.Remove('Command Timeout')
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($neutral.get_ConnectionString())
    $builder['ApplicationIntent'] = 'ReadOnly'
    $builder['Pooling'] = $false
    $builder['MultipleActiveResultSets'] = $false
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.get_ConnectionString())
    try {
        $connection.Open()
        $missing = @()
        foreach ($requirement in $requirements) {
            $command = $connection.CreateCommand()
            try {
                $command.CommandTimeout = 15
                $command.CommandText = if ($requirement.Kind -eq 'Table') {
                    "SELECT CASE WHEN OBJECT_ID(@object, N'U') IS NULL THEN 0 ELSE 1 END"
                } else {
                    'SELECT CASE WHEN COL_LENGTH(@object, @column) IS NULL THEN 0 ELSE 1 END'
                }
                [void]$command.Parameters.AddWithValue('@object', $requirement.Object)
                if ($requirement.Kind -eq 'Column') { [void]$command.Parameters.AddWithValue('@column', $requirement.Column) }
                if ([int]$command.ExecuteScalar() -ne 1) { $missing += $requirement }
            } finally { $command.Dispose() }
        }
    } finally { $connection.Dispose() }
} catch {
    Write-Error 'Academic schema metadata could not be checked. Verify the target connection and read permission locally; connection details are not printed.'
    exit 3
} finally {
    $rawConnection = $null
}

if ($missing.Count -gt 0) {
    Write-Output "NOT_READY: $($missing.Count) required academic schema objects are absent."
    $missing | Sort-Object Migration, Object, Column | ForEach-Object {
        $name = if ($_.Kind -eq 'Table') { $_.Object } else { "$($_.Object).$($_.Column)" }
        Write-Output "MISSING $name -> db/changes/$($_.Migration)"
    }
    exit 2
}

Write-Output "READY: all $($requirements.Count) required academic schema objects are present."
exit 0
