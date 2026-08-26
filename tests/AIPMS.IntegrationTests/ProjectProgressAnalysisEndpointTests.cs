using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Projects.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AIPMS.IntegrationTests;

public sealed class ProjectProgressAnalysisEndpointTests : IClassFixture<ProjectProgressAnalysisEndpointTests.AnalysisWebApplicationFactory>
{
    public class AnalysisWebApplicationFactory : AipmsWebApplicationFactory
    {
        public TestProjectProgressDataReader DataReader { get; } = new();
        public TestProjectAccessService ProjectAccessService { get; } = new();

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectProgressDataReader>();
                services.AddSingleton<IProjectProgressDataReader>(DataReader);

                services.RemoveAll<AIPMS.Application.Abstractions.Security.IProjectAccessService>();
                services.AddSingleton<AIPMS.Application.Abstractions.Security.IProjectAccessService>(ProjectAccessService);
            });
        }
    }

    private readonly AnalysisWebApplicationFactory _factory;

    public ProjectProgressAnalysisEndpointTests(AnalysisWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProgressAnalysis_AuthorizedClient_Returns200OKWithAnalysisDto()
    {
        var projectId = 101L;
        _factory.ProjectAccessService.CanAccess = true;
        _factory.DataReader.FactsMap[projectId] = new ProjectProgressFacts(
            ProjectId: projectId,
            ProjectStatus: "ACTIVE",
            TeamId: 1,
            TeamMemberCount: 4,
            Milestones: new List<MilestoneFact>
            {
                new(1, "Milestone 1", "COMPLETED", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 1)
            },
            Tasks: new List<TaskFact>
            {
                new(1, 1, "Task 1", "DONE", "NORMAL", DateTime.UtcNow.AddDays(-10), DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(-6), 1)
            },
            ProgressReports: Array.Empty<ProgressReportFact>(),
            Meetings: Array.Empty<MeetingFact>());

        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync($"api/v1/projects/{projectId}/ai/progress-analysis");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<ProjectProgressAnalysisDto>();
        Assert.NotNull(dto);
        Assert.Equal(projectId, dto.ProjectId);
        Assert.Equal("SUFFICIENT", dto.DataStatus);
        Assert.Equal("LOW", dto.RiskLevel);
        Assert.Equal("PROVISIONAL_RULE_BASELINE_1.0", dto.RuleVersion);
    }

    [Fact]
    public async Task GetProgressAnalysis_UnauthorizedUser_Returns403Forbidden()
    {
        var projectId = 102L;
        _factory.ProjectAccessService.CanAccess = false;

        var client = _factory.CreateAuthenticatedClient(99, "other@aipms.test", "Other", AppRoles.Student);

        var response = await client.GetAsync($"api/v1/projects/{projectId}/ai/progress-analysis");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProgressAnalysis_NonexistentProject_Returns404NotFound()
    {
        var projectId = 9999L;
        _factory.ProjectAccessService.CanAccess = true;
        _factory.DataReader.FactsMap.Remove(projectId);

        var client = _factory.CreateAuthenticatedClient(10, "student@aipms.test", "Student", AppRoles.Student);

        var response = await client.GetAsync($"api/v1/projects/{projectId}/ai/progress-analysis");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class TestProjectProgressDataReader : IProjectProgressDataReader
{
    public Dictionary<long, ProjectProgressFacts> FactsMap { get; } = new();

    public Task<ProjectProgressFacts?> GetProjectProgressFactsAsync(long projectId, CancellationToken cancellationToken) =>
        Task.FromResult(FactsMap.GetValueOrDefault(projectId));
}

public class TestProjectAccessService : AIPMS.Application.Abstractions.Security.IProjectAccessService
{
    public bool CanAccess { get; set; } = true;

    public Task<bool> CanAccessAsync(long userId, long projectId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CanAccess);
}
