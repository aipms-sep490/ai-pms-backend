using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.IntegrationTests;

public sealed class SystemEndpointTests : IClassFixture<AipmsWebApplicationFactory>
{
    private readonly HttpClient _client;

    public SystemEndpointTests(AipmsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetSystem_ReturnsApiStatus()
    {
        var response = await _client.GetAsync("/api/system");
        var body = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
    }

    [Fact]
    public async Task AnalyzeProgress_InvalidTaskCounts_ReturnsProblemDetails()
    {
        var input = new AnalyzeProgressCommand(2, 3, 0, 0.5m);

        var response = await _client.PostAsJsonAsync("/api/ai/insights/progress", input);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Validation failed.", problem.Title);
        Assert.True(problem.Extensions.ContainsKey("traceId"));
        Assert.True(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public async Task AnalyzeProgress_ValidInput_ReturnsAnalysisFromHandler()
    {
        var input = new AnalyzeProgressCommand(10, 4, 2, 0.4m);

        var response = await _client.PostAsJsonAsync("/api/ai/insights/progress", input);
        var result = await response.Content.ReadFromJsonAsync<ProgressAnalysisResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("HIGH", result.RiskLevel);
        Assert.Equal(0.4m, result.OverdueTaskRatio);
    }
}
