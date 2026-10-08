using AIPMS.Application.Features.StudentQualifications.DTOs;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AIPMS.Api.OpenApi;

public sealed class DepartContractSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(VerifyStudentQualificationRequest)
            && context.Type != typeof(DecideStudentQualificationRequest)) return;
        if (schema.Properties.TryGetValue("expectedConcurrencyToken", out var token))
            token.Description = "Send concurrencyToken from the evidence being reviewed. A stale token returns 409 without mutation. "
                + "During compatibility rollout only, omission reviews the current version inside the transaction; "
                + "it cannot detect evidence changed since the client last read it. New clients must send this field.";
    }
}
