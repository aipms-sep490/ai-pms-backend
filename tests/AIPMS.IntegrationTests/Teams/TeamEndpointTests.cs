using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private static async System.Threading.Tasks.Task<T> BodyAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static System.Threading.Tasks.Task<TeamDto> CreateAsync(HttpClient client, TeamScenario scenario, string code = "TEAM") =>
        CreateCoreAsync(client, scenario, code);

    private static async System.Threading.Tasks.Task<TeamDto> CreateCoreAsync(HttpClient client, TeamScenario scenario, string code) =>
        await BodyAsync<TeamDto>(await client.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = scenario.SemesterId, code, name = "My Team", description = "Capstone" }));

    private static async System.Threading.Tasks.Task<TeamInvitationDto> InviteAsync(HttpClient client, long teamId, long userId) =>
        await BodyAsync<TeamInvitationDto>(await client.PostAsJsonAsync($"/api/v1/teams/{teamId}/invitations",
            new { invitedUserId = userId, message = "Join us" }));

    private static System.Threading.Tasks.Task<HttpResponseMessage> AcceptAsync(HttpClient client, long id) =>
        client.PostAsync($"/api/v1/teams/invitations/{id}/accept", null);

    [Fact]
    public async Task Create_invite_accept_transfer_leave_rejoin_preserves_history_and_one_leader()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        using var b = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(a, s);
        Assert.Equal("FORMING", team.Status);
        Assert.Single(team.Members, m => m.IsLeader);
        var invite = await InviteAsync(a, team.Id, s.Students[1]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(b, invite.Id));
        Assert.Equal("ELIGIBLE", team.Status);
        Assert.True(team.Eligibility.CanRegister);
        Assert.StartsWith($"v-{s.PeriodId}-", team.Eligibility.PolicyVersion);
        Assert.NotEqual("test-v1", team.Eligibility.PolicyVersion);
        team = await BodyAsync<TeamDto>(await a.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader",
            new { newLeaderUserId = s.Students[1] }));
        Assert.Equal(s.Students[1], Assert.Single(team.Members, m => m.IsLeader).UserId);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/api/v1/teams/{team.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.GetAsync($"/api/v1/teams/current?academicSemesterId={s.SemesterId}")).StatusCode);
        team = await BodyAsync<TeamDto>(await b.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal("FORMING", team.Status);
        var rejoin = await InviteAsync(b, team.Id, s.Students[0]);
        await BodyAsync<TeamDto>(await AcceptAsync(a, rejoin.Id));
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.TeamMembers.CountAsync(m => m.TeamId == team.Id));
        Assert.Equal(1, await context.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null));
        Assert.True(await context.AuditLogs.AnyAsync(log => log.Action == "TEAM_LEFT" && log.EntityId == team.Id.ToString()));
    }

    [Fact]
    public async Task Only_leader_can_manage_and_invitation_recipient_can_respond()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        using var b = app.CreateAuthenticatedClient(s.Students[1]);
        using var outsider = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateAsync(a, s);
        var invitation = await InviteAsync(a, team.Id, s.Students[1]);
        Assert.Equal(HttpStatusCode.Forbidden, (await AcceptAsync(outsider, invitation.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/teams/{team.Id}")).StatusCode);
        await BodyAsync<TeamDto>(await AcceptAsync(b, invitation.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PutAsJsonAsync($"/api/v1/teams/{team.Id}",
            new { name = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[3] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.DeleteAsync($"/api/v1/teams/{team.Id}/members/{s.Students[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/v1/teams/{team.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync($"/api/v1/teams/invitations?teamId={team.Id}")).StatusCode);
    }

    [Fact]
    public async Task Expired_duplicate_cancelled_and_rejected_invitations_are_enforced()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        using var b = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(a, s);
        var invitation = await InviteAsync(a, team.Id, s.Students[1]);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1] })).StatusCode);
        await using (var context = database.CreateContext())
            await context.TeamInvitations.Where(i => i.Id == invitation.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.ExpiresAt, TeamDatabaseFixture.Now));
        Assert.Equal(HttpStatusCode.Conflict, (await AcceptAsync(b, invitation.Id)).StatusCode);
        var inbox = await BodyAsync<PagedResult<TeamInvitationDto>>(await b.GetAsync("/api/v1/teams/invitations"));
        Assert.Equal("EXPIRED", Assert.Single(inbox.Items).Status);
        invitation = await InviteAsync(a, team.Id, s.Students[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await b.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/reject", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AcceptAsync(b, invitation.Id)).StatusCode);
        invitation = await InviteAsync(a, team.Id, s.Students[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await a.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await AcceptAsync(b, invitation.Id)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_accepts_for_last_seat_do_not_exceed_capacity()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, maxMembers: 2);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var b = app.CreateAuthenticatedClient(s.Students[1]);
        using var c = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateAsync(leader, s);
        var first = await InviteAsync(leader, team.Id, s.Students[1]);
        var second = await InviteAsync(leader, team.Id, s.Students[2]);
        var responses = await Task.WhenAll(AcceptAsync(b, first.Id), AcceptAsync(c, second.Id));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        Assert.Equal(2, await context.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.LeftAt == null));
        Assert.Equal(1, await context.TeamInvitations.CountAsync(i => i.TeamId == team.Id && i.Status == "ACCEPTED"));
    }

    [Fact]
    public async Task Concurrent_accepts_to_different_teams_allow_only_one_membership_per_semester()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leaderA = app.CreateAuthenticatedClient(s.Students[0]);
        using var leaderB = app.CreateAuthenticatedClient(s.Students[1]);
        using var recipient = app.CreateAuthenticatedClient(s.Students[2]);
        var teamA = await CreateAsync(leaderA, s, "A");
        var teamB = await CreateAsync(leaderB, s, "B");
        var a = await InviteAsync(leaderA, teamA.Id, s.Students[2]);
        var b = await InviteAsync(leaderB, teamB.Id, s.Students[2]);
        var responses = await Task.WhenAll(AcceptAsync(recipient, a.Id), AcceptAsync(recipient, b.Id));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.TeamMembers.CountAsync(m => m.UserId == s.Students[2] && m.LeftAt == null));
    }

    [Fact]
    public async Task Same_major_team_becomes_eligible_when_minimum_size_is_met()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        using var sameMajor = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateAsync(a, s);
        var invitation = await InviteAsync(a, team.Id, s.Students[2]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(sameMajor, invitation.Id));
        Assert.Equal("ELIGIBLE", team.Status);
        Assert.True(team.Eligibility.CanRegister);
        Assert.Empty(team.Eligibility.Reasons);
    }

    [Theory]
    [InlineData("SUBMITTED")]
    [InlineData("ACTIVE")]
    [InlineData("COMPLETED")]
    public async Task Project_lifecycle_locks_roster(string status)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        using var b = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(a, s);
        var invitation = await InviteAsync(a, team.Id, s.Students[1]);
        await using (var context = database.CreateContext())
        {
            context.Projects.Add(new Project
            {
                TeamId = team.Id, Code = $"P{team.Id}", Title = "Capstone", Status = status,
                CreatedBy = s.Students[0], RegisteredAt = TeamDatabaseFixture.Now
            });
            await context.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await AcceptAsync(b, invitation.Id)).StatusCode);
        var result = await BodyAsync<TeamDto>(await a.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.True(result.Eligibility.RosterLocked);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new { name = "Changed" })).StatusCode);
    }

    [Fact]
    public async Task Closed_window_or_missing_policy_prevents_mutations()
    {
        var s = await database.SeedAsync();
        using var unconfiguredApp = new TeamTestFactory(database, s, configured: false);
        using var unconfigured = unconfiguredApp.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Conflict, (await unconfigured.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "FAIL", name = "No Policy" })).StatusCode);
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(a, s);
        await using (var context = database.CreateContext())
            await context.ProjectPeriods.Where(p => p.Id == s.PeriodId)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.EndAt, TeamDatabaseFixture.Now));
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsync($"/api/v1/teams/{team.Id}/eligibility/refresh", null)).StatusCode);
        var result = await BodyAsync<TeamDto>(await a.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.False(result.Eligibility.CanRegister);
    }

    [Fact]
    public async Task Duplicate_creation_and_cross_organization_invitations_are_rejected()
    {
        var s = await database.SeedAsync();
        var other = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(a, s);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "SECOND", name = "Duplicate" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = other.Students[0] })).StatusCode);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_team_and_leader_creation()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAudit: true);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.InternalServerError, (await a.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "ROLLBACK", name = "Rollback" })).StatusCode);
        await using var context = database.CreateContext();
        Assert.False(await context.Teams.AnyAsync(t => t.AcademicSemesterId == s.SemesterId));
        Assert.False(await context.TeamMembers.AnyAsync(m => m.AcademicSemesterId == s.SemesterId));
    }

    [Fact]
    public async Task Auth_validation_pagination_and_update_contracts()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/teams/current?academicSemesterId=1")).StatusCode);
        using var staff = app.CreateAuthenticatedClient(s.Students[0], roles: ["DEPARTMENT_STAFF"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/teams/current?academicSemesterId=1")).StatusCode);
        using var a = app.CreateAuthenticatedClient(s.Students[0]);
        var invalid = await a.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = 0, code = "", name = "" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/v1/teams/invitations?pageSize=101")).StatusCode);
        var team = await CreateAsync(a, s);
        team = await BodyAsync<TeamDto>(await a.PutAsJsonAsync($"/api/v1/teams/{team.Id}", new { name = "Renamed", description = "New" }));
        Assert.Equal("Renamed", team.Name);
        await InviteAsync(a, team.Id, s.Students[1]);
        await InviteAsync(a, team.Id, s.Students[2]);
        var page = await BodyAsync<PagedResult<TeamInvitationDto>>(
            await a.GetAsync($"/api/v1/teams/invitations?teamId={team.Id}&page=2&pageSize=1"));
        Assert.Single(page.Items);
        Assert.Equal(2, page.TotalCount);
    }
}

internal sealed class TeamTestFactory(TeamDatabaseFixture database, TeamScenario scenario,
    int? maxMembers = null, bool configured = true, bool failAudit = false,
    int? minMembers = null, string? failAuditAction = null,
    Action<IServiceCollection>? customizeServices = null) : AipmsWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        using (var context = database.CreateContext())
        {
            var period = context.ProjectPeriods.Find(scenario.PeriodId);
            if (period != null)
            {
                if (configured)
                {
                    period.MinTeamSize = minMembers ?? (period.MinTeamSize ?? 2);
                    period.MaxTeamSize = maxMembers ?? (period.MaxTeamSize ?? 3);
                }
                else
                {
                    period.MinTeamSize = null;
                    period.MaxTeamSize = null;
                }
                context.SaveChanges();
            }
        }
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var values = new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = database.ConnectionString };
            if (configured)
            {
                var prefix = $"TeamFormation:Periods:{scenario.PeriodId}:";
                values[prefix + "InvitationHours"] = "24";
                values[prefix + "Version"] = "test-v1";
            }
            config.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new TeamClock());
            if (failAudit)
            {
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail, FailingAudit>();
            }
            if (failAuditAction is not null)
            {
                var auditType = services.Last(d => d.ServiceType == typeof(IAuditTrail)).ImplementationType!;
                services.RemoveAll<IAuditTrail>();
                services.AddScoped<IAuditTrail>(sp => new SelectiveFailingAudit(
                    (IAuditTrail)ActivatorUtilities.CreateInstance(sp, auditType), failAuditAction));
            }
            customizeServices?.Invoke(services);
        });
    }

    private sealed class TeamClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(TeamDatabaseFixture.Now);
    }

    private sealed class FailingAudit : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected audit failure");
    }

    private sealed class SelectiveFailingAudit(IAuditTrail inner, string action) : IAuditTrail
    {
        public Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            entry.Action == action
                ? throw new InvalidOperationException("Injected audit failure")
                : inner.RecordAsync(entry, cancellationToken);
    }
}
