using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed class TeamPolicyIntegrationTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private static async Task<T> BodyAsync<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<TeamDto> CreateAsync(HttpClient client, long semesterId, string code = "POLICY") =>
        await BodyAsync<TeamDto>(await client.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = semesterId, code, name = "Policy Team", description = "Test" }));

    private static async Task<TeamInvitationDto> InviteAsync(HttpClient client, long teamId, long userId) =>
        await BodyAsync<TeamInvitationDto>(await client.PostAsJsonAsync($"/api/v1/teams/{teamId}/invitations",
            new { invitedUserId = userId, message = "Join" }));

    private static Task<HttpResponseMessage> AcceptAsync(HttpClient client, long id) =>
        client.PostAsync($"/api/v1/teams/invitations/{id}/accept", null);

    [Fact]
    public async Task CreateTeam_UsesProjectPeriodPolicy()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 3;
            p.MaxTeamSize = 5;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);

        // When MinTeamSize is 3, 1-member team is FORMING
        var team = await CreateAsync(client0, s.SemesterId, "FORM1");
        Assert.Equal("FORMING", team.Status);
        Assert.False(team.Eligibility.CanRegister);
        Assert.Contains("TOO_FEW_MEMBERS", team.Eligibility.Reasons);

        // When MinTeamSize is 1, 1-member team is ELIGIBLE
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 1;
            await context.SaveChangesAsync();
        }

        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);
        var team2 = await CreateAsync(client1, s.SemesterId, "ELIG1");
        Assert.Equal("ELIGIBLE", team2.Status);
        Assert.True(team2.Eligibility.CanRegister);
    }

    [Fact]
    public async Task InviteMember_UsesDbMaxTeamSize()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 2;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);

        var team = await CreateAsync(client0, s.SemesterId, "MAX2");
        var invite = await InviteAsync(client0, team.Id, s.Students[1]);
        await BodyAsync<TeamDto>(await AcceptAsync(client1, invite.Id));

        // Team now has 2 members, matching MaxTeamSize = 2. Inviting a 3rd should fail.
        var invite2Response = await client0.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[2], message = "Join" });
        Assert.Equal(HttpStatusCode.Conflict, invite2Response.StatusCode);
    }

    [Fact]
    public async Task InviteMember_WhenAtMaxTeamSize_Rejects()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 1;
            p.MaxTeamSize = 1;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await CreateAsync(client0, s.SemesterId, "MAX1");
        // Team has 1 member, which is MaxTeamSize = 1.
        var inviteResponse = await client0.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" });
        Assert.Equal(HttpStatusCode.Conflict, inviteResponse.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_RechecksDbMaxTeamSize()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 2;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);
        using var client2 = app.CreateAuthenticatedClient(s.Students[2]);

        var team = await CreateAsync(client0, s.SemesterId, "RECHECK");
        // Issue 2 invitations while team size is 1
        var invite1 = await InviteAsync(client0, team.Id, s.Students[1]);
        var invite2 = await InviteAsync(client0, team.Id, s.Students[2]);

        // First acceptance succeeds (brings team to 2)
        var accepted1 = await AcceptAsync(client1, invite1.Id);
        Assert.Equal(HttpStatusCode.OK, accepted1.StatusCode);

        // Second acceptance must recheck MaxTeamSize and fail
        var accepted2 = await AcceptAsync(client2, invite2.Id);
        Assert.Equal(HttpStatusCode.Conflict, accepted2.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_WhenConcurrentAcceptanceWouldExceedMax_OnlyOneSucceeds()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 2;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);
        using var client2 = app.CreateAuthenticatedClient(s.Students[2]);

        var team = await CreateAsync(client0, s.SemesterId, "CONCURR");
        var invite1 = await InviteAsync(client0, team.Id, s.Students[1]);
        var invite2 = await InviteAsync(client0, team.Id, s.Students[2]);

        // Accept concurrently
        var task1 = AcceptAsync(client1, invite1.Id);
        var task2 = AcceptAsync(client2, invite2.Id);
        var responses = await Task.WhenAll(task1, task2);

        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, successCount);
        Assert.Equal(1, conflictCount);

        await using var verifyContext = database.CreateContext();
        var memberCount = await verifyContext.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.LeftAt == null);
        Assert.Equal(2, memberCount);
    }

    [Fact]
    public async Task Eligibility_WhenBelowDbMinTeamSize_IsFalse()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 3;
            p.MaxTeamSize = 5;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);

        var team = await CreateAsync(client0, s.SemesterId, "BELOWMIN");
        var invite = await InviteAsync(client0, team.Id, s.Students[1]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(client1, invite.Id));

        // Team has 2 members, but MinTeamSize = 3
        Assert.Equal("FORMING", team.Status);
        Assert.False(team.Eligibility.CanRegister);
        Assert.Contains("TOO_FEW_MEMBERS", team.Eligibility.Reasons);
    }

    [Fact]
    public async Task MinDistinctMajors_GreaterThanOne_CreateTeam_Returns409UnsupportedHybrid()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 2;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);

        var response = await client0.PostAsJsonAsync("/api/v1/teams",
            new { academicSemesterId = s.SemesterId, code = "HYBRIDFAIL", name = "Hybrid Team", description = "Test" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Interdisciplinary team formation requires Hybrid policy configuration", body);
    }

    [Fact]
    public async Task MinDistinctMajors_GreaterThanOne_Invite_Returns409UnsupportedHybrid()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(client0, s.SemesterId, "INVITEHYB");

        // Mutate DB policy to MinDistinctMajors = 2
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinDistinctMajors = 2;
            await context.SaveChangesAsync();
        }

        var response = await client0.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[1], message = "Join" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Interdisciplinary team formation requires Hybrid policy configuration", body);
    }

    [Fact]
    public async Task MinDistinctMajors_GreaterThanOne_Accept_Returns409UnsupportedHybrid()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(client0, s.SemesterId, "ACCEPTHYB");
        var invite = await InviteAsync(client0, team.Id, s.Students[1]);

        // Mutate DB policy to MinDistinctMajors = 2 before accept
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinDistinctMajors = 2;
            await context.SaveChangesAsync();
        }

        var response = await AcceptAsync(client1, invite.Id);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Interdisciplinary team formation requires Hybrid policy configuration", body);
    }

    [Fact]
    public async Task MinDistinctMajors_GreaterThanOne_EligibilityOrRegistration_Returns409UnsupportedHybrid()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        using var client1 = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(client0, s.SemesterId, "ELIGREF");
        var invite = await InviteAsync(client0, team.Id, s.Students[1]);
        await BodyAsync<TeamDto>(await AcceptAsync(client1, invite.Id));

        // Mutate DB policy to MinDistinctMajors = 2
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinDistinctMajors = 2;
            await context.SaveChangesAsync();
        }

        var refreshResponse = await client0.PostAsync($"/api/v1/teams/{team.Id}/eligibility/refresh", null);
        Assert.Equal(HttpStatusCode.Conflict, refreshResponse.StatusCode);
        var body = await refreshResponse.Content.ReadAsStringAsync();
        Assert.Contains("Interdisciplinary team formation requires Hybrid policy configuration", body);
    }

    [Fact]
    public async Task SingleMajor_DifferentMajorCandidate_IsRejected()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]); // SE major

        var team = await CreateAsync(client0, s.SemesterId, "DIFFMAJ");
        // Try to invite Student 4 (IS major)
        var response = await client0.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations",
            new { invitedUserId = s.Students[4], message = "Join cross major" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("All team members must belong to the same major", body);
    }

    [Fact]
    public async Task SingleMajor_EligibilityRequiresSameMajor()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(client0, s.SemesterId, "SMELIG");

        // Directly insert a different major member into DB
        await using (var context = database.CreateContext())
        {
            var member = new TeamMember
            {
                TeamId = team.Id,
                AcademicSemesterId = s.SemesterId,
                UserId = s.Students[4], // IS major
                IsLeader = false,
                JoinedAt = DateTime.UtcNow.AddDays(-1)
            };
            context.TeamMembers.Add(member);
            await context.SaveChangesAsync();
        }

        // Fetch team
        var getResponse = await client0.GetAsync($"/api/v1/teams/{team.Id}");
        var currentTeam = await BodyAsync<TeamDto>(getResponse);

        Assert.False(currentTeam.Eligibility.CanRegister);
        Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", currentTeam.Eligibility.Reasons);
    }

    [Fact]
    public async Task PolicyVersion_ChangesWhenDbPolicyChanges_IgnoringStaleConfig()
    {
        var s = await database.SeedAsync();
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MinTeamSize = 2;
            p.MaxTeamSize = 4;
            p.MinDistinctMajors = 1;
            p.UpdatedAt = new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        using var app = new TeamTestFactory(database, s);
        using var client0 = app.CreateAuthenticatedClient(s.Students[0]);

        var team = await CreateAsync(client0, s.SemesterId, "VERA");
        var versionA = team.Eligibility.PolicyVersion;
        Assert.NotNull(versionA);
        Assert.Equal($"v-{s.PeriodId}-2-4-1", versionA);
        Assert.NotEqual("test-v1", versionA);

        // Mutate an UNRELATED ProjectPeriod field (e.g. Name, MaxProjectsPerSupervisor, UpdatedAt) while team policy stays identical
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.Name = "Unrelated Period Name Changed";
            p.MaxProjectsPerSupervisor = 10;
            p.UpdatedAt = new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        var teamUnrelated = await BodyAsync<TeamDto>(await client0.GetAsync($"/api/v1/teams/{team.Id}"));
        var versionAfterUnrelated = teamUnrelated.Eligibility.PolicyVersion;
        Assert.Equal(versionA, versionAfterUnrelated); // Unrelated mutation does NOT alter effective team policy version

        // Mutate DB team policy (MaxTeamSize)
        await using (var context = database.CreateContext())
        {
            var p = await context.ProjectPeriods.FindAsync(s.PeriodId);
            p!.MaxTeamSize = 5;
            p.UpdatedAt = new DateTime(2026, 9, 11, 13, 0, 0, DateTimeKind.Utc);
            await context.SaveChangesAsync();
        }

        var teamUpdated = await BodyAsync<TeamDto>(await client0.GetAsync($"/api/v1/teams/{team.Id}"));
        var versionB = teamUpdated.Eligibility.PolicyVersion;
        Assert.NotNull(versionB);
        Assert.Equal($"v-{s.PeriodId}-2-5-1", versionB);
        Assert.NotEqual(versionA, versionB);

        // Read again with unchanged DB policy -> version is stable and same
        var teamUnchanged = await BodyAsync<TeamDto>(await client0.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal(versionB, teamUnchanged.Eligibility.PolicyVersion);
    }
}
