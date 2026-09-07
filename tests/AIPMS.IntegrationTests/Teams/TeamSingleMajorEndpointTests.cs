using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Teams;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    private const string MajorMismatchDetail = "All team members must belong to the same major as the team leader.";

    private static async Task AssertMajorConflictAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
        Assert.Equal(MajorMismatchDetail, problem.Detail);
        Assert.False(string.IsNullOrWhiteSpace(problem.Type));
    }

    [Theory]
    [InlineData(0, 1, "SE")]
    [InlineData(4, 5, "IS")]
    public async Task Both_demo_majors_can_form_eligible_single_major_teams(int leaderIndex, int memberIndex, string majorCode)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[leaderIndex]);
        using var member = app.CreateAuthenticatedClient(s.Students[memberIndex]);
        var team = await CreateAsync(leader, s, majorCode);
        var invitation = await InviteAsync(leader, team.Id, s.Students[memberIndex]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(member, invitation.Id));
        Assert.Equal("ELIGIBLE", team.Status);
        Assert.True(team.Eligibility.CanRegister);
        Assert.Empty(team.Eligibility.Reasons);
        var expectedMajor = majorCode == "SE" ? s.SeMajorId : s.IsMajorId;
        Assert.All(team.Members, m => Assert.Equal(expectedMajor, m.MajorId));
        team = await BodyAsync<TeamDto>(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader",
            new { newLeaderUserId = s.Students[memberIndex] }));
        Assert.Equal(s.Students[memberIndex], Assert.Single(team.Members, m => m.IsLeader).UserId);
        Assert.True(team.Eligibility.CanRegister);

        await using var db = database.CreateContext();
        var majors = await db.Majors.Where(m => m.Id == s.SeMajorId || m.Id == s.IsMajorId)
            .Select(m => new { m.Code, m.DepartmentId }).ToArrayAsync();
        Assert.Equal(new[] { "IS", "SE" }, majors.Select(m => m.Code).OrderBy(c => c).ToArray());
        Assert.Single(majors.Select(m => m.DepartmentId).Distinct());
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    public async Task Cross_major_invite_fails_early_without_invitation_or_audit(int leaderIndex, int inviteeIndex)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[leaderIndex]);
        var team = await CreateAsync(leader, s);
        await AssertMajorConflictAsync(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[inviteeIndex] }));
        await using var db = database.CreateContext();
        Assert.False(await db.TeamInvitations.AnyAsync(i => i.TeamId == team.Id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == team.Id.ToString() && a.Action == "TEAM_INVITED"));
        Assert.Equal(1, await db.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.LeftAt == null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accept_rechecks_recipient_and_current_leader_major_after_invitation(bool leaderChanged)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var invitee = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(leader, s);
        var invitation = await InviteAsync(leader, team.Id, s.Students[1]);
        await using var db = database.CreateContext();
        var changedUserId = s.Students[leaderChanged ? 0 : 1];
        await db.Users.Where(u => u.Id == changedUserId).ExecuteUpdateAsync(u => u.SetProperty(x => x.MajorId, s.IsMajorId));
        await AssertMajorConflictAsync(await AcceptAsync(invitee, invitation.Id));
        Assert.Equal("PENDING", await db.TeamInvitations.Where(i => i.Id == invitation.Id).Select(i => i.Status).SingleAsync());
        Assert.False(await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == s.Students[1]));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == team.Id.ToString() && a.Action == "TEAM_INVITATION_ACCEPTED"));
    }

    [Fact]
    public async Task Legacy_cross_major_invitation_cannot_bypass_accept_guard()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var invitee = app.CreateAuthenticatedClient(s.Students[4]);
        var team = await CreateAsync(leader, s);
        await using var db = database.CreateContext();
        var invitation = new TeamInvitation
        {
            TeamId = team.Id, InvitedBy = s.Students[0], InvitedUserId = s.Students[4], Status = "PENDING",
            CreatedAt = TeamDatabaseFixture.Now, ExpiresAt = TeamDatabaseFixture.Now.AddHours(1)
        };
        db.TeamInvitations.Add(invitation);
        await db.SaveChangesAsync();
        await AssertMajorConflictAsync(await AcceptAsync(invitee, invitation.Id));
        Assert.False(await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == s.Students[4]));
        Assert.Equal("PENDING", await db.TeamInvitations.Where(i => i.Id == invitation.Id).Select(i => i.Status).SingleAsync());
    }

    [Fact]
    public async Task Legacy_mixed_roster_cannot_expand_or_transfer_and_can_be_repaired_by_removing_mismatch()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        await using var db = database.CreateContext();
        await db.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.MajorId, s.IsMajorId));
        team = await BodyAsync<TeamDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.False(team.Eligibility.CanRegister);
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", team.Eligibility.Reasons);
        await AssertMajorConflictAsync(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[2] }));
        await AssertMajorConflictAsync(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader",
            new { newLeaderUserId = s.Students[1] }));
        team = await BodyAsync<TeamDto>(await leader.PostAsync($"/api/v1/teams/{team.Id}/eligibility/refresh", null));
        Assert.Equal("FORMING", team.Status);
        Assert.Equal(s.Students[0], Assert.Single(team.Members, m => m.IsLeader).UserId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await leader.DeleteAsync($"/api/v1/teams/{team.Id}/members/{s.Students[1]}")).StatusCode);
        var invitation = await InviteAsync(leader, team.Id, s.Students[2]);
        using var replacement = app.CreateAuthenticatedClient(s.Students[2]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(replacement, invitation.Id));
        Assert.True(team.Eligibility.CanRegister);
        Assert.All(team.Members, m => Assert.Equal(s.SeMajorId, m.MajorId));
    }
}
