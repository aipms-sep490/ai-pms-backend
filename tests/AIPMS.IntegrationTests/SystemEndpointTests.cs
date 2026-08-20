using System.Net;
using System.Net.Http.Json;
using AIPMS.Api.Controllers;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;
using Microsoft.AspNetCore.Mvc;

namespace AIPMS.IntegrationTests;

public sealed class SystemEndpointTests : IClassFixture<AipmsWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly HttpClient _authenticatedClient;

    public SystemEndpointTests(AipmsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
        _authenticatedClient = factory.CreateAuthenticatedClient();
    }

    [Fact]
    public async Task GetSystem_ReturnsApiStatus()
    {
        var response = await _client.GetAsync("/api/v1/system");
        var body = await response.Content.ReadFromJsonAsync<SystemInfoResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
    }

    [Fact]
    public async Task GetCurrentUser_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_ReturnsClaims()
    {
        var response = await _authenticatedClient.GetAsync("/api/v1/auth/me");
        var user = await response.Content.ReadFromJsonAsync<AuthUserDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(user);
        Assert.Equal(1001, user.Id);
        Assert.Equal("student@aipms.test", user.Email);
        Assert.Contains("STUDENT", user.Roles);
    }

    [Fact]
    public async Task GetProjectLifecycle_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/projects/lifecycle");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnalyzeProgress_InvalidTaskCounts_ReturnsProblemDetails()
    {
        var input = new AnalyzeProgressCommand(2, 3, 0, 0.5m);

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/v1/ai/insights/progress",
            input);
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

        var response = await _authenticatedClient.PostAsJsonAsync(
            "/api/v1/ai/insights/progress",
            input);
        var result = await response.Content.ReadFromJsonAsync<ProgressAnalysisResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal("HIGH", result.RiskLevel);
        Assert.Equal(0.4m, result.OverdueTaskRatio);
    }
}
