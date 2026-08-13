[CmdletBinding()]
param(
    [string]$ConnectionName = "ConnectionStrings:DefaultConnection",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

Push-Location $repositoryRoot

try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to restore the local dotnet tools."
    }

    $scaffoldArguments = @(
        "ef",
        "dbcontext",
        "scaffold",
        "Name=$ConnectionName",
        "Microsoft.EntityFrameworkCore.SqlServer",
        "--project", "src/AIPMS.Infrastructure",
        "--startup-project", "src/AIPMS.Api",
        "--context", "AipmsDbContext",
        "--context-dir", "Persistence/Generated",
        "--output-dir", "Persistence/Generated/Models",
        "--context-namespace", "AIPMS.Infrastructure.Persistence.Generated",
        "--namespace", "AIPMS.Infrastructure.Persistence.Generated.Models",
        "--no-onconfiguring"
    )

    if ($Force) {
        $scaffoldArguments += "--force"
    }

    & dotnet @scaffoldArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Database scaffolding failed."
    }
}
finally {
    Pop-Location
}
