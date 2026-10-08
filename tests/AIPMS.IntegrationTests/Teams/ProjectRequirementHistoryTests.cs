using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    [Fact]
    public async Task Requirements_stale_check_and_rounds_preserve_proposal_requirements_and_decisions()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        var check = await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("PASS", check.Result);
        var url = $"/api/v1/projects/{project.Id}";
        ProjectMajorRequirementInput[] requirements = [new(s.Team.SeMajorId, 1, 2, "Round one software"), new(s.Team.IsMajorId, 1, 1, "Business")];
        var saved = await Body<ProjectRequirementsDto>(await leader.PutAsJsonAsync(url + "/major-requirements", new ReplaceProjectRequirementsRequest(requirements, project.ConcurrencyToken)));
        Assert.NotEqual(project.ConcurrencyToken, saved.ConcurrencyToken);
        Assert.Equal("STALE", (await Body<TeamEligibilityCheckDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}/eligibility"))).Freshness);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(url + "/submit", new { concurrencyToken = saved.ConcurrencyToken })).StatusCode);
        project = await Transition(leader, project with { ConcurrencyToken = saved.ConcurrencyToken }, "submit");
        var first = await Body<ProjectReviewHistoryDto>(await leader.GetAsync(url + "/review-snapshots"));
        var oldRound = Assert.Single(first.Items);
        Assert.True(oldRound.ProposalAvailable);
        Assert.Equal("Hybrid proposal", oldRound.Evidence!.Proposal!.Title);
        Assert.Equal("Round one software", oldRound.Evidence!.ProjectRequirements!.Single(x => x.MajorId == s.Team.SeMajorId).Responsibility);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(url + "/major-requirements", new ReplaceProjectRequirementsRequest(requirements, project.ConcurrencyToken))).StatusCode);
        project = await Transition(lead, project, "start-review");
        var review = await Decide(other, project.Id, await Review(leader, project.Id), "REJECTED");
        project = await Transition(lead, project with { ConcurrencyToken = review.ConcurrencyToken }, "revision");
        requirements[0] = requirements[0] with { Responsibility = "Round two software" };
        saved = await Body<ProjectRequirementsDto>(await leader.PutAsJsonAsync(url + "/major-requirements", new ReplaceProjectRequirementsRequest(requirements, project.ConcurrencyToken)));
        project = await Body<ProjectDto>(await leader.PutAsJsonAsync(url, new UpdateProjectDraftRequest(saved.ConcurrencyToken,
            "Revised proposal", "New description", "New objectives", "New problem", "New output",
            [s.Team.SeMajorId, s.Team.IsMajorId], "Education", ["Dotnet"], ["Revision"])));
        project = await Transition(leader, project, "resubmit");
        var history = await Body<ProjectReviewHistoryDto>(await leader.GetAsync(url + "/review-snapshots"));
        Assert.Equal(2, history.TotalCount); Assert.Equal([2, 1], history.Items.Select(x => x.SubmissionNumber));
        Assert.Equal("Round two software", history.Items[0].Evidence!.ProjectRequirements!.Single(x => x.MajorId == s.Team.SeMajorId).Responsibility);
        Assert.Equal("Round one software", history.Items[1].Evidence!.ProjectRequirements!.Single(x => x.MajorId == s.Team.SeMajorId).Responsibility);
        Assert.Equal("Revised proposal", history.Items[0].Evidence!.Proposal!.Title);
        Assert.Equal("Hybrid proposal", history.Items[1].Evidence!.Proposal!.Title);
        Assert.All(history.Items[0].Decisions, d => Assert.Equal("PENDING", d.Decision));
        Assert.Contains(history.Items[1].Decisions, d => d.DepartmentId == s.OtherDepartment && d.Decision == "REJECTED");
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync(url + "/department-decisions",
            new DepartmentDecisionRequest(oldRound.Id, project.ConcurrencyToken, "APPROVED", "Old round"))).StatusCode);
        var page = await Body<ProjectReviewHistoryDto>(await leader.GetAsync(url + "/review-snapshots?page=2&pageSize=1"));
        Assert.Equal(oldRound.Id, Assert.Single(page.Items).Id);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.Set<ProjectRegistrationSnapshot>().CountAsync(x => x.ProjectId == project.Id));
    }

    [Fact]
    public async Task Project_quota_fails_recheck_even_when_team_quota_passes()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        var saved = await Body<ProjectRequirementsDto>(await leader.PutAsJsonAsync($"/api/v1/projects/{project.Id}/major-requirements",
            new ReplaceProjectRequirementsRequest([new(s.Team.SeMajorId, 2, 2, "Two engineers"), new(s.Team.IsMajorId, 1, 1, "Business")], project.ConcurrencyToken)));
        var check = await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        Assert.Equal("FAIL", check.Result); Assert.Contains(check.Issues, x => x.RuleCode == "PROJECT_MAJOR_QUOTA" && x.MajorId == s.Team.SeMajorId);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/submit", new { concurrencyToken = saved.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/eligibility/lock", new { checkId = check.CheckId })).StatusCode);
    }

    [Fact]
    public async Task Requirements_edit_racing_submit_has_one_winner_and_no_mixed_snapshot()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var staff = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        await Body<TeamEligibilityCheckDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/check", null));
        var submit = leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/submit", new { concurrencyToken = project.ConcurrencyToken });
        var edit = staff.PutAsJsonAsync($"/api/v1/projects/{project.Id}/major-requirements", new ReplaceProjectRequirementsRequest(
            [new(s.Team.SeMajorId, 1, 2, "New engineering"), new(s.Team.IsMajorId, 1, 1, "New business")], project.ConcurrencyToken));
        var responses = await Task.WhenAll(submit, edit);
        Assert.Single(responses, x => x.IsSuccessStatusCode);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        await using var db = database.CreateContext();
        var snapshots = await db.Set<ProjectRegistrationSnapshot>().Where(x => x.ProjectId == project.Id).ToListAsync();
        if (responses[0].IsSuccessStatusCode)
        {
            Assert.Single(snapshots);
            Assert.False(await db.ProjectMajorRequirements.AnyAsync(x => x.ProjectId == project.Id));
        }
        else
        {
            Assert.Empty(snapshots);
            Assert.Equal(2, await db.ProjectMajorRequirements.CountAsync(x => x.ProjectId == project.Id));
            Assert.Equal("DRAFT", (await db.Projects.FindAsync(project.Id))!.Status);
        }
    }
}
