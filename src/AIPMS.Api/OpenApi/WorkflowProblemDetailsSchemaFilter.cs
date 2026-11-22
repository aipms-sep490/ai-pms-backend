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
            Type = "string",
            Nullable = true,
            Description = "Stable workflow blocker code when available. Does not change the HTTP status; never parse detail to infer it.",
            Example = new OpenApiString("STALE_CONCURRENCY_TOKEN")
        };
        schema.Properties["errorCode"] = new OpenApiSchema
        {
            Type = "string",
            Nullable = true,
            Description = "Legacy alias of code retained for backward compatibility."
        };
        schema.Properties["message"] = new OpenApiSchema
        {
            Type = "string",
            Nullable = true,
            Description = "Human-readable error message aligned with detail/title."
        };
        schema.Properties["traceId"] = new OpenApiSchema { Type = "string" };
        schema.Properties["correlationId"] = new OpenApiSchema
        {
            Type = "string",
            Nullable = true,
            Description = "Request correlation identifier (X-Correlation-Id header or traceId fallback)."
        };
        schema.Properties["fieldErrors"] = new OpenApiSchema
        {
            Type = "object",
            Nullable = true,
            AdditionalPropertiesAllowed = true,
            AdditionalProperties = new OpenApiSchema
            {
                Type = "array",
                Items = new OpenApiSchema { Type = "string" }
            },
            Description = "Validation errors keyed by field name (omitted on 401/403/404)."
        };
        schema.Properties["blockingRequirements"] = new OpenApiSchema
        {
            Type = "array",
            Nullable = true,
            Items = new OpenApiSchema
            {
                Type = "object",
                Properties = new Dictionary<string, OpenApiSchema>
                {
                    ["code"] = new OpenApiSchema { Type = "string" },
                    ["message"] = new OpenApiSchema { Type = "string" },
                    ["target"] = new OpenApiSchema { Type = "string", Nullable = true }
                }
            },
            Description = "Structured blocking requirements explaining why a workflow transition was rejected (omitted on 401/403/404)."
        };
        schema.Properties["currentState"] = new OpenApiSchema { Type = "string", Nullable = true };
        schema.Properties["expectedState"] = new OpenApiSchema { Type = "string", Nullable = true };
        schema.Properties["policyVersion"] = new OpenApiSchema { Type = "string", Nullable = true };
        schema.Properties["retryable"] = new OpenApiSchema { Type = "boolean", Nullable = true };
    }
}
