using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class PolicyEvaluationSwaggerTests
{
    [Fact]
    public void Swagger_contains_policy_scheme_student_and_existing_evaluation_routes()
    {
        using var app = new AipmsWebApplicationFactory();
        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        foreach (var path in new[] {
            "/api/v1/project-periods/{id}/effective-policy", "/api/v1/project-periods/{id}/policy",
            "/api/v1/project-periods/{id}/policy-versions", "/api/v1/evaluation-schemes", "/api/v1/evaluation-schemes/{id}",
            "/api/v1/evaluation-schemes/{id}/publish", "/api/v1/evaluation-schemes/{id}/versions",
            "/api/v1/projects/{projectId}/students/{studentId}/result", "/api/v1/projects/{projectId}/students/{studentId}/result/preview",
            "/api/v1/projects/{projectId}/evaluation-assignments", "/api/v1/evaluations/{id}/finalize",
            "/api/v1/projects/{projectId}/result" })
            Assert.True(document.Paths.ContainsKey(path), path);
        Assert.Contains(document.Components.Schemas.Values, schema => schema.Properties.ContainsKey("calculationRule"));
    }
}
