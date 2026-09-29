using System.Text.Json;

namespace AIPMS.IntegrationTests;

public sealed class RemediationOpenApiTests
{
    [Fact]
    public async Task Swagger_exposes_candidates_and_additive_problem_code_without_removing_workflow_routes()
    {
        using var app = new AipmsWebApplicationFactory();
        using var client = app.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var paths = document.RootElement.GetProperty("paths");
        foreach (var route in new[] { "eligible-evaluators", "evaluation-assignments", "actions", "archive", "history", "final-submission", "result" })
            Assert.Contains(paths.EnumerateObject(), p => p.Name.StartsWith("/api/v1/projects/", StringComparison.Ordinal) && p.Name.EndsWith("/" + route, StringComparison.Ordinal));
        var candidate = paths.GetProperty("/api/v1/projects/{projectId}/eligible-evaluators").GetProperty("get");
        foreach (var parameter in new[] { "periodId", "page", "pageSize" })
            Assert.Contains(candidate.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == parameter);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("ProblemDetails").GetProperty("properties").TryGetProperty("code", out _));
        Assert.True(schemas.GetProperty("EligibleEvaluatorDto").GetProperty("properties").TryGetProperty("evaluationTypes", out _));
    }
}
