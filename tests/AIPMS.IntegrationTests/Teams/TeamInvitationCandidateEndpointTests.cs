using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Teams.DTOs;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class TeamEndpointTests
{
    private static string CandidatesUrl(long teamId, string? query = null) =>
        $"/api/v1/teams/{teamId}/invitation-candidates" + (query is null ? "" : "?" + query);

    private static async Task<PagedResult<TeamInvitationCandidateDto>> CandidatesAsync(HttpClient client, long teamId, string? query = null) =>
        await BodyAsync<PagedResult<TeamInvitationCandidateDto>>(await client.GetAsync(CandidatesUrl(teamId, query)));

    [Fact]
    public async Task Candidates_search_identity_fields_page_stably_and_return_only_safe_metadata()
    {
        var s = await database.SeedAsync();
        await database.SeedAsync();
        string email;
        await using (var db = database.CreateContext())
        {
            var users = await db.Users.Where(u => s.Students.Contains(u.Id)).ToListAsync();
            foreach (var user in users) user.FullName = "Same Name";
            var target = users.Single(u => u.Id == s.Students[1]);
            target.StudentCode = "DEMO-SE-001";
            email = target.Email;
            await db.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(leader, s);
        var page = await CandidatesAsync(leader, team.Id);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(s.Students.Skip(1).Take(3), page.Items.Select(i => i.UserId));
        Assert.All(page.Items, i => { Assert.Equal(s.SeMajorId, i.MajorId); Assert.True(i.CanInvite); Assert.Equal("NONE", i.InvitationStatus); });
        Assert.Equal(s.Students[2], Assert.Single((await CandidatesAsync(leader, team.Id, "page=2&pageSize=1")).Items).UserId);
        Assert.Empty((await CandidatesAsync(leader, team.Id, "page=4&pageSize=1")).Items);
        foreach (var search in new[] { email, " DEMO-SE-001 " })
            Assert.Equal(s.Students[1], Assert.Single((await CandidatesAsync(leader, team.Id, "search=" + Uri.EscapeDataString(search))).Items).UserId);
        Assert.Equal(3, (await CandidatesAsync(leader, team.Id, "search=Same%20Name")).TotalCount);
        Assert.Equal(3, (await CandidatesAsync(leader, team.Id, "search=%20%20")).TotalCount);
        Assert.Empty((await CandidatesAsync(leader, team.Id, "search=%25_%27")).Items);
        var response = await leader.GetAsync(CandidatesUrl(team.Id));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var properties = json.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(new[] { "userId", "fullName", "email", "studentCode", "majorId", "majorCode", "majorName",
            "invitationStatus", "pendingInvitationId", "pendingInvitationExpiresAt", "canInvite" }.Order(), properties);
    }

    [Theory]
    [InlineData("PENDING", 1, true)]
    [InlineData("PENDING", 0, false)]
    [InlineData("PENDING", -1, false)]
    [InlineData("PENDING", null, false)]
    [InlineData("REJECTED", 1, false)]
    [InlineData("CANCELLED", 1, false)]
    [InlineData("EXPIRED", -1, false)]
    public async Task Candidates_show_only_live_pending_invitation_without_mutating_history(string status, int? hours, bool pending)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(leader, s);
        var invitation = await InviteAsync(leader, team.Id, s.Students[1]);
        await using (var db = database.CreateContext())
        {
            var stored = (await db.TeamInvitations.FindAsync(invitation.Id))!;
            stored.Status = status;
            stored.ExpiresAt = hours.HasValue ? TeamDatabaseFixture.Now.AddHours(hours.Value) : null;
            await db.SaveChangesAsync();
        }
        var candidate = (await CandidatesAsync(leader, team.Id)).Items.Single(i => i.UserId == s.Students[1]);
        Assert.Equal(!pending, candidate.CanInvite);
        Assert.Equal(pending ? "PENDING" : "NONE", candidate.InvitationStatus);
        Assert.Equal(pending ? invitation.Id : (long?)null, candidate.PendingInvitationId);
        Assert.Equal(pending ? TeamDatabaseFixture.Now.AddHours(1) : (DateTime?)null, candidate.PendingInvitationExpiresAt);
        await using (var db = database.CreateContext())
            Assert.Equal(status, (await db.TeamInvitations.FindAsync(invitation.Id))!.Status);
        var send = await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations", new { invitedUserId = s.Students[1] });
        Assert.Equal(pending ? HttpStatusCode.Conflict : HttpStatusCode.OK, send.StatusCode);
    }

    [Fact]
    public async Task Candidates_do_not_expose_other_teams_invitations()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var otherLeader = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateAsync(leader, s, "FIRST");
        var otherTeam = await CreateAsync(otherLeader, s, "OTHER");
        await InviteAsync(otherLeader, otherTeam.Id, s.Students[1]);
        var candidate = (await CandidatesAsync(leader, team.Id)).Items.Single(i => i.UserId == s.Students[1]);
        Assert.True(candidate.CanInvite);
        Assert.Equal("NONE", candidate.InvitationStatus);
        Assert.Null(candidate.PendingInvitationId);
        await InviteAsync(leader, team.Id, candidate.UserId);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("role")]
    [InlineData("noMajor")]
    [InlineData("differentMajor")]
    [InlineData("departmentMismatch")]
    [InlineData("inactiveMajor")]
    [InlineData("inactiveDepartment")]
    [InlineData("inactiveOrganization")]
    public async Task Candidates_recheck_student_and_academic_eligibility(string change)
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(leader, s);
        await using (var db = database.CreateContext())
        {
            var user = (await db.Users.FindAsync(s.Students[1]))!;
            if (change == "inactive") user.Status = "SUSPENDED";
            if (change == "role") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == user.Id).ToListAsync());
            if (change == "noMajor") user.MajorId = null;
            if (change == "differentMajor") user.MajorId = s.IsMajorId;
            if (change == "departmentMismatch") user.DepartmentId = null;
            var major = await db.Majors.Include(m => m.Department).ThenInclude(d => d.Organization).SingleAsync(m => m.Id == s.SeMajorId);
            if (change == "inactiveMajor") major.IsActive = false;
            if (change == "inactiveDepartment") major.Department.IsActive = false;
            if (change == "inactiveOrganization") major.Department.Organization.IsActive = false;
            await db.SaveChangesAsync();
        }
        if (change.StartsWith("inactive", StringComparison.Ordinal) && change != "inactive")
            Assert.Equal(HttpStatusCode.Conflict, (await leader.GetAsync(CandidatesUrl(team.Id))).StatusCode);
        else
            Assert.DoesNotContain((await CandidatesAsync(leader, team.Id)).Items, i => i.UserId == s.Students[1]);
    }

    [Fact]
    public async Task Candidates_authorize_current_leader_and_reject_spoofed_roles_and_invalid_queries()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        using var outsider = app.CreateAuthenticatedClient(s.Students[2]);
        using var staff = app.CreateAuthenticatedClient(s.Students[0], roles: ["DEPARTMENT_STAFF"]);
        using var anonymous = app.CreateClient();
        var team = await CreateAsync(leader, s);
        await BodyAsync<TeamDto>(await AcceptAsync(member, (await InviteAsync(leader, team.Id, s.Students[1])).Id));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(CandidatesUrl(team.Id))).StatusCode);
        foreach (var client in new[] { member, outsider, staff })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(CandidatesUrl(team.Id))).StatusCode);
        foreach (var query in new[] { "page=0", "page=2147483647", "pageSize=101", "search=" + new string('a', 256) })
            Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(CandidatesUrl(team.Id, query))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.GetAsync(CandidatesUrl(long.MaxValue))).StatusCode);
        await BodyAsync<TeamDto>(await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/leader", new { newLeaderUserId = s.Students[1] }));
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.GetAsync(CandidatesUrl(team.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync(CandidatesUrl(team.Id))).StatusCode);
        await using (var db = database.CreateContext())
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Students[1]).ToListAsync());
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(CandidatesUrl(team.Id))).StatusCode);
    }

    [Fact]
    public async Task Candidate_search_does_not_reserve_membership_and_stale_invite_is_rejected()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var candidateClient = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(leader, s, "FIRST");
        var candidate = (await CandidatesAsync(leader, team.Id)).Items.Single(i => i.UserId == s.Students[1]);
        await CreateAsync(candidateClient, s, "SECOND");
        Assert.DoesNotContain((await CandidatesAsync(leader, team.Id)).Items, i => i.UserId == candidate.UserId);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations", new { invitedUserId = candidate.UserId })).StatusCode);
    }

    [Fact]
    public async Task Prior_semester_membership_and_ended_current_membership_do_not_hide_candidate()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        var team = await CreateAsync(leader, s);
        await using (var db = database.CreateContext())
        {
            var currentSemester = (await db.AcademicSemesters.FindAsync(s.SemesterId))!;
            var priorTeam = new M.Team { Code = Guid.NewGuid().ToString("N"), Name = "Previous team", Status = "DISBANDED", CreatedBy = s.Students[1],
                AcademicSemester = new M.AcademicSemester { OrganizationId = currentSemester.OrganizationId, Code = Guid.NewGuid().ToString("N"), Name = "Previous", Status = "CLOSED",
                    StartDate = currentSemester.StartDate.AddYears(-1), EndDate = currentSemester.EndDate.AddYears(-1) } };
            db.Teams.Add(priorTeam);
            await db.SaveChangesAsync();
            db.TeamMembers.Add(new() { TeamId = priorTeam.Id, AcademicSemesterId = priorTeam.AcademicSemesterId, UserId = s.Students[1], IsLeader = true });
            await db.SaveChangesAsync();
        }
        Assert.Contains((await CandidatesAsync(leader, team.Id)).Items, i => i.UserId == s.Students[1]);
        await BodyAsync<TeamDto>(await AcceptAsync(member, (await InviteAsync(leader, team.Id, s.Students[1])).Id));
        Assert.DoesNotContain((await CandidatesAsync(leader, team.Id)).Items, i => i.UserId == s.Students[1]);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PostAsync($"/api/v1/teams/{team.Id}/leave", null)).StatusCode);
        var candidate = (await CandidatesAsync(leader, team.Id)).Items.Single(i => i.UserId == s.Students[1]);
        Assert.True(candidate.CanInvite);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("closed")]
    [InlineData("policy")]
    [InlineData("locked")]
    public async Task Candidates_reject_context_where_invitations_are_unavailable(string reason)
    {
        var s = await database.SeedAsync();
        using var setup = new TeamTestFactory(database, s);
        using var creator = setup.CreateAuthenticatedClient(s.Students[0]);
        var team = await CreateAsync(creator, s);
        await using (var db = database.CreateContext())
        {
            if (reason == "closed") (await db.ProjectPeriods.FindAsync(s.PeriodId))!.Status = "CLOSED";
            if (reason == "locked") (await db.Teams.FindAsync(team.Id))!.Status = "LOCKED";
            await db.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s, maxMembers: reason == "capacity" ? 1 : 3,
            minMembers: 1, configured: reason != "policy");
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.GetAsync(CandidatesUrl(team.Id))).StatusCode);
    }
}
