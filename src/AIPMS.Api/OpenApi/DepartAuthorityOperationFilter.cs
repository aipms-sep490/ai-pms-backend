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
            || (path.StartsWith("api/v1/academic/project-periods", StringComparison.Ordinal)
                && !path.EndsWith("/policy", StringComparison.Ordinal)
                && !path.EndsWith("/qualification-policy", StringComparison.Ordinal)
                && !(context.ApiDescription.HttpMethod == "PUT" && path.EndsWith("/{periodId}", StringComparison.Ordinal)))))
            operation.Description = "ADMIN_ONLY. Platform administrators manage semester skeletons, period creation/status and defense room scheduling toggle; department staff manage period date extensions, policies and calendars.";
        if (path.StartsWith("api/v1/projects/", StringComparison.Ordinal)
            && (path.EndsWith("/result", StringComparison.Ordinal) || path.EndsWith("/result/preview", StringComparison.Ordinal)))
            operation.Description = "Lead department staff only (platform ADMIN is forbidden with ADMIN_NOT_ACADEMIC_AUTHORITY). "
                + "Project preview reports publication blockers and QD-05 warnings; mutation rechecks the same inputs and confirmation token. Unknown legacy scope is read-only.";
        if (path == "api/v1/dashboards/portfolio/export")
            operation.Description = "Authorized server-scoped CSV (default), XLSX or Unicode PDF. Maximum 10000 project rows; narrow filters if 422 is returned. "
                + "X-Correlation-Id identifies the download audit. Department staff cannot choose another department.";
        if (path.StartsWith("api/v1/supervisor-assignments/", StringComparison.Ordinal) && path.EndsWith("/end", StringComparison.Ordinal))
            operation.Description = "Only ACTIVE projects allow ending an unended non-primary assignment. Owner lecturer or responsible department staff may end it; "
                + "primary supervisors on ACTIVE projects require atomic replacement (/replace, QD-08). COMPLETED/ARCHIVED projects are read-only.";
    }
}
