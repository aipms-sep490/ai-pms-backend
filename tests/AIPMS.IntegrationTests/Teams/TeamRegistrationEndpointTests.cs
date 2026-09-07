using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Teams;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    private async System.Threading.Tasks.Task<TeamDto> EligibleTeamAsync(
        TeamTestFactory app, TeamScenario s, HttpClient leader)
    {
        var team = await CreateAsync(leader, s);
        var invitation = await InviteAsync(leader, team.Id, s.Students[1]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        return await BodyAsync<TeamDto>(await AcceptAsync(member, invitation.Id));
    }

    private async System.Threading.Tasks.Task<HttpResponseMessage> DraftResponseAsync(HttpClient leader, TeamScenario s)
    {
        await using var db = database.CreateContext();
        var majorId = await db.Users.Where(u => u.Id == s.Students[0]).Select(u => u.MajorId).SingleAsync();
        return await leader.PostAsJsonAsync("/api/v1/projects", new
        {
            title = "Team registration regression", description = "Capstone",
            objectives = "Validate registration", problemStatement = "Inconsistent eligibility",
            expectedOutput = "Working application", requiredMajorIds = new[] { majorId!.Value },
            domain = "Education", technologies = new[] { ".NET" }, keywords = new[] { "Management" }
        });
    }

    private static System.Threading.Tasks.Task<HttpResponseMessage> SubmitAsync(HttpClient leader, ProjectDto draft, string endpoint = "submit") =>
        leader.PostAsJsonAsync($"/api/v1/projects/{draft.Id}/{endpoint}", new { concurrencyToken = draft.ConcurrencyToken });

    [Theory]
    [InlineData("INACTIVE_STUDENT", "submit")]
    [InlineData("INACTIVE_STUDENT", "resubmit")]
    [InlineData("REMOVED_ROLE", "submit")]
    [InlineData("INACTIVE_MAJOR", "submit")]
    [InlineData("CLOSED_SEMESTER", "submit")]
    [InlineData("EXPIRED_WINDOW", "submit")]
    [InlineData("DUPLICATE_WINDOW", "submit")]
    [InlineData("CHANGED_MAJOR", "submit")]
    [InlineData("CHANGED_MAJOR", "resubmit")]
    public async Task Submit_rechecks_current_eligibility_instead_of_cached_status(string change, string endpoint)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, s));
        await using (var db = database.CreateContext())
        {
            if (change == "INACTIVE_STUDENT")
                await db.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
            else if (change == "REMOVED_ROLE")
                await db.UserRoles.Where(r => r.UserId == s.Students[1]).ExecuteDeleteAsync();
            else if (change == "INACTIVE_MAJOR")
            {
                var majorId = await db.Users.Where(u => u.Id == s.Students[1]).Select(u => u.MajorId).SingleAsync();
                await db.Majors.Where(m => m.Id == majorId).ExecuteUpdateAsync(m => m.SetProperty(x => x.IsActive, false));
            }
            else if (change == "CLOSED_SEMESTER")
                await db.AcademicSemesters.Where(p => p.Id == s.SemesterId).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, "CLOSED"));
            else if (change == "EXPIRED_WINDOW")
                await db.ProjectPeriods.Where(p => p.Id == s.PeriodId).ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now));
            else if (change == "CHANGED_MAJOR")
                await db.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.MajorId, s.IsMajorId));
            else
            {
                db.ProjectPeriods.Add(new ProjectPeriod
                {
                    AcademicSemesterId = s.SemesterId, Code = "SECOND_REG", Name = "Ambiguous registration",
                    PeriodType = "REGISTRATION", Status = "ACTIVE",
                    StartAt = TeamDatabaseFixture.Now.AddDays(-1), EndAt = TeamDatabaseFixture.Now.AddDays(2)
                });
                await db.SaveChangesAsync();
            }
            if (endpoint == "resubmit")
            {
                await db.Projects.Where(p => p.Id == draft.Id).ExecuteUpdateAsync(p => p.SetProperty(x => x.Status, "REVISION_REQUIRED"));
                var stored = await db.Projects.SingleAsync(p => p.Id == draft.Id);
                draft = draft with { Status = stored.Status, ConcurrencyToken = Convert.ToBase64String(stored.RowVersion) };
            }
            Assert.Equal("ELIGIBLE", await db.Teams.Where(t => t.Id == team.Id).Select(t => t.Status).SingleAsync());
        }
        var result = await SubmitAsync(leader, draft, endpoint);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        if (change == "CHANGED_MAJOR")
            Assert.Contains("TEAM_MUST_BE_SINGLE_MAJOR", await result.Content.ReadAsStringAsync());
        await using var verify = database.CreateContext();
        Assert.Equal(draft.Status, await verify.Projects.Where(p => p.Id == draft.Id).Select(p => p.Status).SingleAsync());
        Assert.False(await verify.ProjectStatusHistories.AnyAsync(h => h.ProjectId == draft.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creating_project_rechecks_member_profile_even_with_cached_eligible_team(bool changedMajor)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        await using (var db = database.CreateContext())
        {
            if (changedMajor)
                await db.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.MajorId, s.IsMajorId));
            else
                await db.Users.Where(u => u.Id == s.Students[1]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await DraftResponseAsync(leader, s)).StatusCode);
        await using var verify = database.CreateContext();
        Assert.False(await verify.Projects.AnyAsync(p => p.TeamId == team.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Submit_fails_closed_when_current_policy_is_missing_or_more_restrictive(bool missing)
    {
        var s = await database.SeedAsync();
        using var initialApp = new TeamTestFactory(database, s);
        using var leader = initialApp.CreateAuthenticatedClient(s.Students[0]);
        await EligibleTeamAsync(initialApp, s, leader);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, s));
        using var changedApp = new TeamTestFactory(database, s, configured: !missing, minMembers: 3);
        using var changedLeader = changedApp.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(changedLeader, draft)).StatusCode);
    }

    [Fact]
    public async Task Same_major_team_supports_configurable_size_without_diversity_requirement()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, maxMembers: 4, minMembers: 3);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        Assert.Equal("FORMING", team.Status);
        var third = await InviteAsync(leader, team.Id, s.Students[2]);
        using var member = app.CreateAuthenticatedClient(s.Students[2]);
        team = await BodyAsync<TeamDto>(await AcceptAsync(member, third.Id));
        Assert.Equal("ELIGIBLE", team.Status);
        Assert.True(team.Eligibility.CanRegister);
    }

    [Fact]
    public async Task Accept_audit_failure_rolls_back_membership_invitation_and_eligibility()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAuditAction: "TEAM_INVITATION_ACCEPTED");
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(leader, s);
        var invite = await InviteAsync(leader, team.Id, s.Students[1]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        Assert.Equal(HttpStatusCode.InternalServerError, (await AcceptAsync(member, invite.Id)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.TeamMembers.AnyAsync(m => m.TeamId == team.Id && m.UserId == s.Students[1] && m.LeftAt == null));
        Assert.Equal("PENDING", await db.TeamInvitations.Where(i => i.Id == invite.Id).Select(i => i.Status).SingleAsync());
        Assert.Equal("FORMING", await db.Teams.Where(t => t.Id == team.Id).Select(t => t.Status).SingleAsync());
    }

    [Fact]
    public async Task Transfer_audit_failure_restores_original_leader()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAuditAction: "TEAM_LEADER_TRANSFERRED");
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        var response = await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader", new { newLeaderUserId = s.Students[1] });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await using var db = database.CreateContext();
        var leaders = await db.TeamMembers.Where(m => m.TeamId == team.Id && m.IsLeader && m.LeftAt == null).Select(m => m.UserId).ToArrayAsync();
        Assert.Equal([s.Students[0]], leaders);
    }

    [Fact]
    public async Task Submit_audit_failure_rolls_back_project_status_and_history()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAuditAction: "PROJECT_SUBMITTED");
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        await EligibleTeamAsync(app, s, leader);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, s));
        Assert.Equal(HttpStatusCode.InternalServerError, (await SubmitAsync(leader, draft)).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("DRAFT", await db.Projects.Where(p => p.Id == draft.Id).Select(p => p.Status).SingleAsync());
        Assert.False(await db.ProjectStatusHistories.AnyAsync(h => h.ProjectId == draft.Id));
    }

    [Fact]
    public async Task Create_project_audit_failure_rolls_back_entire_draft()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s, failAuditAction: "PROJECT_DRAFT_CREATED");
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await EligibleTeamAsync(app, s, leader);
        Assert.Equal(HttpStatusCode.InternalServerError, (await DraftResponseAsync(leader, s)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.Projects.AnyAsync(p => p.TeamId == team.Id));
    }

    [Fact]
    public async Task Submit_holds_roster_lock_until_project_status_is_committed()
    {
        var s = await database.SeedAsync();
        var checkpoint = new RegistrationCheckpoint();
        using var app = new TeamTestFactory(database, s, customizeServices: services =>
        {
            services.RemoveAll<ITeamRegistrationGuard>();
            services.AddScoped<ITeamRegistrationGuard>(sp =>
                new PausingRegistrationGuard(ActivatorUtilities.CreateInstance<TeamRegistrationGuard>(sp), checkpoint));
        });
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await EligibleTeamAsync(app, s, leader);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, s));
        checkpoint.Enabled = true;
        var submission = SubmitAsync(leader, draft);
        await checkpoint.Validated.Task.WaitAsync(TimeSpan.FromSeconds(15));
        var leave = member.PostAsync($"/api/v1/teams/{team.Id}/leave", null);
        try
        {
            // The member cannot commit a departure between validation and project submission.
            Assert.NotSame(leave, await Task.WhenAny(leave, Task.Delay(150)));
        }
        finally { checkpoint.Release.TrySetResult(); }
        Assert.Equal(HttpStatusCode.OK, (await submission.WaitAsync(TimeSpan.FromSeconds(15))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leave.WaitAsync(TimeSpan.FromSeconds(15))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(2, await db.TeamMembers.CountAsync(m => m.TeamId == team.Id && m.LeftAt == null));
    }

    [Fact]
    public async Task Member_departure_before_submit_prevents_registration()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await EligibleTeamAsync(app, s, leader);
        var draft = await BodyAsync<ProjectDto>(await DraftResponseAsync(leader, s));
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsync($"/api/v1/teams/{team.Id}/leave", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(leader, draft)).StatusCode);
    }

    [Fact]
    public async Task Test_database_cleanup_does_not_drop_the_supplied_connection_catalog()
    {
        var originalCatalog = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(database.ConnectionString).InitialCatalog;
        await using var separate = new IsolatedSqlDatabase();
        await separate.StartAsync(database.ConnectionString);
        var separateCatalog = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(separate.ConnectionString).InitialCatalog;
        Assert.NotEqual(originalCatalog, separateCatalog);
        Assert.Matches("^AI_PMS_TEST_[a-f0-9]{32}$", separateCatalog);
        await separate.DisposeAsync();
        // The fixture database (the supplied catalog) must remain readable after cleanup.
        await using var original = database.CreateContext();
        Assert.True(await original.Roles.AnyAsync(r => r.Code == "STUDENT"));
    }

    private sealed class RegistrationCheckpoint
    {
        public bool Enabled { get; set; }
        public TaskCompletionSource Validated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class PausingRegistrationGuard(ITeamRegistrationGuard inner, RegistrationCheckpoint checkpoint) : ITeamRegistrationGuard
    {
        public Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) =>
            inner.InTransactionAsync(action, ct);
        public async Task ValidateAsync(long teamId, CancellationToken ct)
        {
            await inner.ValidateAsync(teamId, ct);
            if (!checkpoint.Enabled) return;
            checkpoint.Validated.TrySetResult();
            await checkpoint.Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
        }
    }
}
