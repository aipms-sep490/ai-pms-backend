using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AIPMS.Api.OpenApi;

public sealed class WorkflowProblemDetailsSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!typeof(ProblemDetails).IsAssignableFrom(context.Type)) return;
        schema.Properties["code"] = new OpenApiSchema
        {
            Type = "string", Nullable = true,
            Description = "Stable workflow blocker code when available. Does not change the HTTP status; never parse detail to infer it.",
            Example = new OpenApiString("STALE_CONCURRENCY_TOKEN")
        };
        schema.Properties["traceId"] = new OpenApiSchema { Type = "string" };
    }
}
