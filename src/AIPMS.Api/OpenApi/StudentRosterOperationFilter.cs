using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AIPMS.Api.OpenApi;

public sealed class StudentRosterOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath;
        if (path == "api/v1/teams/export")
        {
            operation.Description = "ACTIVE ADMIN only. Current team members in the required semester. Department/major filters apply to individual current member profiles, "
                + "so interdisciplinary teams may be partial. Maximum 10000 rows, otherwise 422. No matches returns headers only. "
                + "Deterministic team/leader/student ordering. No-store; X-Correlation-ID identifies the export audit.";
            foreach (var parameter in operation.Parameters)
                if (parameter.Name == "semesterId") parameter.Required = true;
            operation.Responses["200"].Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = new()
                { Schema = new OpenApiSchema { Type = "string", Format = "binary" } }
            };
        }
        else if (path == "api/v1/users/curriculum-import/preview")
            operation.Description = "ACTIVE ADMIN only. Multipart file: UTF-8 comma CSV or single-sheet XLSX, 5 MiB, 500 rows. "
                + "Headers MSSV/studentCode and Khung/curriculumCode. Existing students only; blank curriculum skips. "
                + "Preview returns per-row errors and expectedConcurrencyToken; does not mutate data.";
        else if (path == "api/v1/users/curriculum-import/commit")
            operation.Description = "ACTIVE ADMIN only. Submit 1-500 UPDATE/UNCHANGED rows from preview. "
                + "Each student identity, role and rowversion is rechecked transactionally. Stale rows return 409; "
                + "any failure rolls back the entire batch including audit. Empty curriculum cannot erase existing values.";
    }
}
