using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed partial class PolicyEvaluationEndpointTests
{
    [Fact]
    public async Task Depart_D01_result_readiness_agrees_with_scoring_before_and_after_publication()
    {
        var (s, major) = await Seed();
        using var app = new EvaluationFactory(database);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff);
        using var lecturer = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var scheme = await Scheme(staff, s, major);
        var url = $"/api/v1/projects/{s.ProjectId}/governance";
        var resultUrl = $"/api/v1/projects/{s.ProjectId}/result";
        var missing = await Body<ProjectGovernanceDto>(await staff.GetAsync(url));
        Assert.False(missing.Readiness.CanPublishResult);
        Assert.Contains(missing.Blockers, x => x.StartsWith("MISSING_EVALUATOR:", StringComparison.Ordinal));
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "COMMON"), 8);
        await Evaluate(staff, lecturer, s, scheme.Components.Single(c => c.Scope == "INDIVIDUAL"), 6);
        var preview = await Body<ProjectResultPreviewDto>(await staff.GetAsync(resultUrl + "/preview"));
        var ready = await Body<ProjectGovernanceDto>(await staff.GetAsync(url));
        Assert.True(preview.CanPublish);
        Assert.Equal(preview.CanPublish, ready.Readiness.CanPublishResult);
        await Body<ProjectResultDto>(await staff.PostAsJsonAsync(resultUrl, new PublishProjectResultRequest(preview.ConfirmationToken)));
        var published = await Body<ProjectGovernanceDto>(await staff.GetAsync(url));
        Assert.Equal("PUBLISHED", published.ResultPublicationStatus);
        Assert.False(published.Readiness.CanPublishResult);
        Assert.DoesNotContain("MANAGE_GOVERNANCE", published.AllowedActions);
    }
}
