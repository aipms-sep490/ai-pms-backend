using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AIPMS.Api.OpenApi;

public sealed class DepartAuthorityOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? "";
        var mutation = context.ApiDescription.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE";
        if (mutation && (path.StartsWith("api/v1/academic/semesters", StringComparison.Ordinal)
            || path.StartsWith("api/v1/academic/project-periods", StringComparison.Ordinal)
                && !path.EndsWith("/policy", StringComparison.Ordinal)))
            operation.Description = "ADMIN_ONLY. Department staff may read academic structure but cannot mutate semesters, project periods or qualification policy.";
        if (path.StartsWith("api/v1/projects/", StringComparison.Ordinal)
            && (path.EndsWith("/result", StringComparison.Ordinal) || path.EndsWith("/result/preview", StringComparison.Ordinal)))
            operation.Description = "Cross-department project publication requires ADMIN. Staff may publish a student result only in their frozen department scope. "
                + "Project preview reports publication blockers; mutation rechecks the same inputs and confirmation token. Unknown legacy scope is read-only.";
        if (path == "api/v1/dashboards/portfolio/export")
            operation.Description = "Authorized server-scoped CSV (default), XLSX or Unicode PDF. Maximum 10000 project rows; narrow filters if 422 is returned. "
                + "X-Correlation-Id identifies the download audit. Department staff cannot choose another department.";
        if (path.StartsWith("api/v1/supervisor-assignments/", StringComparison.Ordinal) && path.EndsWith("/end", StringComparison.Ordinal))
            operation.Description = "Only ACTIVE projects allow ending an unended assignment. Owner lecturer, platform Admin or the assignment's responsible department may end it. "
                + "COMPLETED/ARCHIVED projects are read-only. Replacement is separately restricted to the responsible department.";
    }
}
