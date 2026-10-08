using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Disciplines.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    [Fact]
    public async Task Structured_responsibilities_invalidate_checks_and_snapshot_survives_revision()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var staff = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        var url = $"/api/v1/teams/{team.Id}/major-requirements/{s.Team.SeMajorId}/responsibilities";
        var before = await Body<ResponsibilityListDto>(await leader.GetAsync(url));
        Assert.True(before.IsAvailable); Assert.Empty(before.Items);
        var input = new ReplaceResponsibilitiesRequest(before.ConcurrencyToken!, [new(" Design backend ", 0), new("Implement", 1)]);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PutAsJsonAsync(url, input)).StatusCode);
        var results = await Task.WhenAll(leader.PutAsJsonAsync(url, input), leader.PutAsJsonAsync(url, input));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var saved = await Body<ResponsibilityListDto>(results.Single(r => r.IsSuccessStatusCode));
        Assert.Equal("Design backend", saved.Items[0].Content);
        Assert.Equal("STALE", (await Body<TeamEligibilityCheckDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}/eligibility"))).Freshness);
        project = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        project = await Transition(leader, project, "submit");
        var projectUrl = $"/api/v1/projects/{project.Id}/major-requirements/{s.Team.SeMajorId}/responsibilities";
        var frozen = await Body<ResponsibilityListDto>(await leader.GetAsync(projectUrl));
        Assert.True(frozen.IsSnapshot); Assert.Equal(2, frozen.Items.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(url, input with { ConcurrencyToken = saved.ConcurrencyToken! })).StatusCode);
        project = await Transition(staff, project, "start-review");
        project = await Transition(staff, project, "revision");
        var editable = await Body<ResponsibilityListDto>(await leader.GetAsync(url));
        await Body<ResponsibilityListDto>(await leader.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(editable.ConcurrencyToken!, [new("Revised backend", 0)])));
        project = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        await Transition(leader, project, "resubmit");
        var history = await Body<ProjectReviewHistoryDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}/review-snapshots"));
        Assert.Equal("Revised backend", Assert.Single(history.Items[0].Evidence!.TeamResponsibilities!).Content);
        Assert.Equal("Design backend", history.Items[1].Evidence!.TeamResponsibilities![0].Content);
        var current = await Body<ResponsibilityListDto>(await leader.GetAsync(projectUrl));
        Assert.Equal("Revised backend", Assert.Single(current.Items).Content);
    }

    [Fact]
    public async Task Clearing_structured_responsibilities_keeps_old_checks_stale()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        var team = await Create(leader, s);
        var original = await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        var url = $"/api/v1/teams/{team.Id}/major-requirements/{s.Team.SeMajorId}/responsibilities";
        var before = await Body<ResponsibilityListDto>(await leader.GetAsync(url));
        var changed = await Body<ResponsibilityListDto>(await leader.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(before.ConcurrencyToken!, [new("Work", 0)])));
        await Body<ResponsibilityListDto>(await leader.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(changed.ConcurrencyToken!, [])));
        var check = await Body<TeamEligibilityCheckDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}/eligibility"));
        Assert.Equal(original.CheckId, check.CheckId); Assert.Equal("STALE", check.Freshness);
    }

    [Fact]
    public async Task Removing_then_restoring_major_does_not_revive_deleted_responsibilities_or_check()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        var team = await Create(leader, s);
        var url = $"/api/v1/teams/{team.Id}/major-requirements/{s.Team.IsMajorId}/responsibilities";
        var before = await Body<ResponsibilityListDto>(await leader.GetAsync(url));
        await Body<ResponsibilityListDto>(await leader.PutAsJsonAsync(url, new ReplaceResponsibilitiesRequest(before.ConcurrencyToken!, [new("Data analysis", 0)])));
        var original = await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        team = await Body<TeamDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}"));
        var single = Scope(s, team.AcademicScope!.ConcurrencyToken) with { ProjectMode = "SINGLE_MAJOR", PrimaryMajorId = s.Team.SeMajorId,
            Requirements = [new(s.Team.SeMajorId, 1, 2, "Software")] };
        team = await Body<TeamDto>(await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", single));
        await Body<TeamDto>(await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", Scope(s, team.AcademicScope!.ConcurrencyToken)));
        Assert.Empty((await Body<ResponsibilityListDto>(await leader.GetAsync(url))).Items);
        var check = await Body<TeamEligibilityCheckDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}/eligibility"));
        Assert.Equal(original.CheckId, check.CheckId); Assert.Equal("STALE", check.Freshness);
    }
}
