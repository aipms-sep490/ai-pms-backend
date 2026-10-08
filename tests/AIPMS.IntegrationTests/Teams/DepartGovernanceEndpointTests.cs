using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    [Fact]
    public async Task Depart_D01_frozen_lead_is_not_lowest_id_and_live_team_changes_do_not_rewrite_scope()
    {
        var original = await SeedAsync();
        var s = original with { LeadDepartment = original.OtherDepartment, OtherDepartment = original.LeadDepartment,
            LeadStaff = original.OtherStaff, OtherStaff = original.LeadStaff };
        Assert.True(s.LeadDepartment > s.OtherDepartment);
        using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s);
        var invite = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null));
        var project = await Transition(lead, await Transition(leader, await Proposal(leader, s), "submit"), "start-review");
        var review = await Decide(other, project.Id, await Review(leader, project.Id));
        review = await Decide(lead, project.Id, review);
        project = await Transition(lead, project with { ConcurrencyToken = review.ConcurrencyToken }, "approve");
        var url = $"/api/v1/projects/{project.Id}/governance";
        var before = await Body<ProjectGovernanceDto>(await leader.GetAsync(url));
        Assert.Equal(s.LeadDepartment, before.LeadDepartment!.DepartmentId);
        Assert.Equal(s.OtherDepartment, Assert.Single(before.ParticipatingDepartments).DepartmentId);
        Assert.False(before.Readiness.CanPublishResult);
        Assert.DoesNotContain("MANAGE_GOVERNANCE", before.AllowedActions);
        await using (var setup = database.CreateContext())
        {
            (await setup.Set<TeamAcademicConfiguration>().FindAsync(team.Id))!.LeadDepartmentId = s.OtherDepartment;
            await setup.SaveChangesAsync();
        }
        var after = await Body<ProjectGovernanceDto>(await lead.GetAsync(url));
        Assert.Equal(s.LeadDepartment, after.LeadDepartment!.DepartmentId);
        var detail = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        Assert.Equal(s.LeadDepartment, detail.AcademicScope!.LeadDepartmentId);
        await using (var setup = database.CreateContext())
        {
            var snapshot = await setup.Set<ProjectRegistrationSnapshot>().SingleAsync(x => x.ProjectId == project.Id);
            snapshot.SnapshotJson = "{}";
            await setup.SaveChangesAsync();
        }
        var unknown = await Body<ProjectGovernanceDto>(await leader.GetAsync(url));
        Assert.Null(unknown.LeadDepartment);
        Assert.Contains("ACADEMIC_SCOPE_UNKNOWN", unknown.Blockers);
        Assert.False(unknown.Readiness.CanSubmitFinal);
        Assert.False(unknown.Readiness.CanPublishResult);
        Assert.DoesNotContain("MANAGE_GOVERNANCE", unknown.AllowedActions);
        detail = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        Assert.Null(detail.AcademicScope);
        var historicalReview = await Review(leader, project.Id);
        var oldSnapshot = Assert.Single(historicalReview.SubmissionHistory!);
        Assert.Equal("UNKNOWN", oldSnapshot.AcademicScopeProvenance);
        var history = await Body<ProjectReviewHistoryDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}/review-snapshots"));
        Assert.Equal(oldSnapshot.Id, Assert.Single(history.Items).Id);
        Assert.Equal("UNKNOWN", history.Items[0].AcademicScopeProvenance);
        Assert.False(history.Items[0].ProposalAvailable);
        var actions = await Body<ProjectWorkflowActionsDto>(await lead.GetAsync($"/api/v1/projects/{project.Id}/actions"));
        foreach (var code in new[] { "start_review", "request_revision", "approve_project", "reject_project" })
            Assert.False(Assert.Single(actions.Actions, a => a.Code == code).Allowed);
    }
}
