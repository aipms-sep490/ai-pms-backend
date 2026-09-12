using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed class WorkflowContextTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private const string ContextUrl = "/api/v1/auth/me/context";

    private static async System.Threading.Tasks.Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static void Allowed(IReadOnlyList<WorkflowActionDto> actions, string code, bool expected, string? reason = null)
    {
        var action = Assert.Single(actions, a => a.Code == code);
        Assert.Equal(expected, action.Allowed);
        if (expected) Assert.Empty(action.Reasons);
        if (reason is not null) Assert.Contains(reason, action.Reasons);
    }

    private async Task AddRole(long userId, string code)
    {
        await using var db = database.CreateContext();
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == code)
            ?? new Role { Code = code, Name = code, IsSystemRole = true };
        db.UserRoles.Add(new UserRole { UserId = userId, Role = role });
        await db.SaveChangesAsync();
    }

    private static async System.Threading.Tasks.Task<TeamDto> CreateTeam(HttpClient client, TeamScenario s) =>
        await Body<TeamDto>(await client.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = s.SemesterId, code = "CTX", name = "Context team" }));

    private static async Task Join(HttpClient leader, HttpClient member, long teamId, long userId)
    {
        var invite = await Body<TeamInvitationDto>(await leader.PostAsJsonAsync($"/api/v1/teams/{teamId}/invitations", new { invitedUserId = userId }));
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invite.Id}/accept", null));
    }

    [Fact]
    public async Task Current_context_reads_persisted_identity_and_grants_without_security_fields_or_caching()
    {
        var s = await database.SeedAsync();
        await using (var db = database.CreateContext())
        {
            var role = await db.Roles.SingleAsync(r => r.Code == "STUDENT");
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id,
                Permission = new Permission { Code = "CONTEXT_TEST", Name = "Context test", IsSystemPermission = false } });
            await db.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(s.Students[0], fullName: "Old JWT name");
        var response = await client.GetAsync(ContextUrl);
        var result = await Body<UserWorkflowContextDto>(response);
        Assert.Equal("Student 0", result.User.FullName);
        Assert.Contains("CONTEXT_TEST", result.User.GrantedPermissions);
        Assert.False(result.User.RequiresTokenRefresh);
        Assert.Equal(["STUDENT"], result.User.EffectiveRoles);
        Assert.Equal(s.SeMajorId, result.Academic.Major!.Id);
        Assert.True(result.Academic.HasEligibleStudentProfile);
        Assert.Equal(s.SemesterId, result.SelectedSemester!.Id);
        Assert.True(Assert.Single(result.Periods).IsOpen);
        Assert.Equal(new DateTimeOffset(TeamDatabaseFixture.Now), result.AsOfUtc);
        Allowed(result.Actions, "create_team", true);
        Allowed(result.Actions, "manage_accounts", false, "ADMIN_REQUIRED");
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Revoked_and_new_roles_require_refresh_and_never_unlock_claim_only_privileges()
    {
        var s = await database.SeedAsync();
        using var app = new TeamTestFactory(database, s);
        using var staleAdmin = app.CreateAuthenticatedClient(s.Students[0], roles: ["ADMIN"]);
        var context = await Body<UserWorkflowContextDto>(await staleAdmin.GetAsync(ContextUrl));
        Assert.True(context.User.RequiresTokenRefresh);
        Assert.Empty(context.User.EffectiveRoles);
        Allowed(context.Actions, "manage_accounts", false);
        Allowed(context.Actions, "create_team", false);
        await AddRole(s.Students[0], "ADMIN");
        using var staleStudent = app.CreateAuthenticatedClient(s.Students[0]);
        context = await Body<UserWorkflowContextDto>(await staleStudent.GetAsync(ContextUrl));
        Assert.True(context.User.RequiresTokenRefresh);
        Allowed(context.Actions, "manage_accounts", false);
        using var refreshed = app.CreateAuthenticatedClient(s.Students[0], roles: ["STUDENT", "ADMIN"]);
        context = await Body<UserWorkflowContextDto>(await refreshed.GetAsync(ContextUrl));
        Assert.False(context.User.RequiresTokenRefresh);
        Allowed(context.Actions, "manage_accounts", true);
    }

    [Theory]
    [InlineData("DEPARTMENT_STAFF", "view_review_queue")]
    [InlineData("LECTURER", "view_supervisor_inbox")]
    public async Task Role_navigation_requires_active_academic_scope(string role, string menu)
    {
        var s = await database.SeedAsync(); await AddRole(s.Students[0], role);
        using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(s.Students[0], roles: ["STUDENT", role]);
        Allowed((await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl))).Actions, menu, true);
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.Include(u => u.Department).SingleAsync(u => u.Id == s.Students[0]);
            user.Department!.IsActive = false;
            await db.SaveChangesAsync();
        }
        var context = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl));
        Assert.Contains("ACADEMIC_SCOPE_INACTIVE", context.Academic.Issues);
        Allowed(context.Actions, menu, false);
        Allowed(context.Actions, "create_team", false, "STUDENT_PROFILE_INELIGIBLE");
    }

    [Fact]
    public async Task Missing_academic_profile_still_returns_own_identity_but_blocks_team_creation()
    {
        var s = await database.SeedAsync();
        await using (var db = database.CreateContext())
        {
            var user = (await db.Users.FindAsync(s.Students[0]))!;
            user.MajorId = null; user.DepartmentId = null; await db.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s); using var client = app.CreateAuthenticatedClient(s.Students[0]);
        var result = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl));
        Assert.Null(result.Academic.Organization);
        Assert.Empty(result.CurrentSemesters);
        Assert.Contains("DEPARTMENT_SCOPE_MISSING", result.Academic.Issues);
        Allowed(result.Actions, "create_team", false, "STUDENT_PROFILE_INELIGIBLE");
    }

    [Fact]
    public async Task Ambiguous_semesters_need_selection_and_foreign_semesters_are_hidden()
    {
        var s = await database.SeedAsync(); var foreign = await database.SeedAsync();
        await using (var db = database.CreateContext())
        {
            var original = (await db.AcademicSemesters.FindAsync(s.SemesterId))!;
            db.AcademicSemesters.Add(new AcademicSemester { OrganizationId = original.OrganizationId, Code = "SECOND",
                Name = "Second", Status = "ACTIVE", StartDate = original.StartDate, EndDate = original.EndDate });
            await db.SaveChangesAsync();
        }
        using var app = new TeamTestFactory(database, s); using var client = app.CreateAuthenticatedClient(s.Students[0]);
        var result = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl));
        Assert.Equal(2, result.CurrentSemesters.Count);
        Assert.Null(result.SelectedSemester);
        Assert.Contains("SEMESTER_SELECTION_REQUIRED", result.SemesterSelectionIssues);
        Allowed(result.Actions, "create_team", false);
        result = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl + $"?academicSemesterId={s.SemesterId}"));
        Allowed(result.Actions, "create_team", true);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ContextUrl + $"?academicSemesterId={foreign.SemesterId}")).StatusCode);
    }

    [Fact]
    public async Task Registration_end_and_unconfigured_policy_disable_creation()
    {
        var s = await database.SeedAsync(); using var app = new TeamTestFactory(database, s, configured: false);
        using var client = app.CreateAuthenticatedClient(s.Students[0]);
        var context = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl));
        Allowed(context.Actions, "create_team", false, "TEAM_POLICY_UNCONFIGURED");
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.PeriodId))!;
            period.MinTeamSize = 2; period.MaxTeamSize = 3; period.EndAt = TeamDatabaseFixture.Now;
            await db.SaveChangesAsync();
        }
        context = await Body<UserWorkflowContextDto>(await client.GetAsync(ContextUrl));
        Assert.False(Assert.Single(context.Periods).IsOpen);
        Allowed(context.Actions, "create_team", false, "REGISTRATION_WINDOW_UNAVAILABLE");
    }

    [Fact]
    public async Task Team_actions_follow_membership_capacity_and_leadership_without_mutating_state()
    {
        var s = await database.SeedAsync(); using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Students[1]);
        using var outsider = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateTeam(leader, s); var url = $"/api/v1/teams/{team.Id}/actions";
        var result = await Body<TeamWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(result.Actions, "invite_member", true);
        Allowed(result.Actions, "create_project_draft", false, "TOO_FEW_MEMBERS");
        Allowed(result.Actions, "leave_team", false, "TRANSFER_LEADERSHIP_FIRST");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(url)).StatusCode);
        await Join(leader, member, team.Id, s.Students[1]);
        result = await Body<TeamWorkflowActionsDto>(await member.GetAsync(url));
        Allowed(result.Actions, "edit_team", false, "TEAM_LEADER_REQUIRED");
        Allowed(result.Actions, "leave_team", true);
        result = await Body<TeamWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(result.Actions, "create_project_draft", true);
        await Join(leader, outsider, team.Id, s.Students[2]);
        result = await Body<TeamWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(result.Actions, "invite_member", false, "TEAM_FULL");
        var current = await Body<UserWorkflowContextDto>(await leader.GetAsync(ContextUrl));
        Assert.Equal(team.Id, current.CurrentTeam!.Id);
        Allowed(current.Actions, "create_team", false, "TEAM_ALREADY_EXISTS");
        await using var db = database.CreateContext();
        var status = (await db.Teams.AsNoTracking().SingleAsync(t => t.Id == team.Id)).Status;
        var auditCount = await db.AuditLogs.CountAsync();
        (await db.ProjectPeriods.FindAsync(s.PeriodId))!.EndAt = TeamDatabaseFixture.Now;
        await db.SaveChangesAsync();
        result = await Body<TeamWorkflowActionsDto>(await leader.GetAsync(url));
        Assert.False(result.CanRegister);
        Assert.Equal(status, (await db.Teams.AsNoTracking().SingleAsync(t => t.Id == team.Id)).Status);
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Project_actions_follow_real_submission_and_supervisor_window_gates()
    {
        var s = await database.SeedAsync(); using var app = new TeamTestFactory(database, s);
        using var leader = app.CreateAuthenticatedClient(s.Students[0]); using var member = app.CreateAuthenticatedClient(s.Students[1]);
        using var outsider = app.CreateAuthenticatedClient(s.Students[2]);
        var team = await CreateTeam(leader, s); await Join(leader, member, team.Id, s.Students[1]);
        var project = await Body<ProjectDto>(await leader.PostAsJsonAsync("/api/v1/projects", new CreateProjectDraftRequest(
            "Context proposal", "Description", "Objectives", "Problem", "Output", [s.SeMajorId], "Education", ["Dotnet"], ["Capstone"])));
        var url = $"/api/v1/projects/{project.Id}/actions";
        var actions = await Body<ProjectWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(actions.Actions, "submit_project", true);
        Allowed(actions.Actions, "resubmit_project", false);
        Allowed((await Body<ProjectWorkflowActionsDto>(await member.GetAsync(url))).Actions, "submit_project", false, "TEAM_LEADER_REQUIRED");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(url)).StatusCode);
        project = await Body<ProjectDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/submit", new { concurrencyToken = project.ConcurrencyToken }));
        actions = await Body<ProjectWorkflowActionsDto>(await leader.GetAsync(url));
        Assert.Equal(project.ConcurrencyToken, actions.ConcurrencyToken);
        Allowed(actions.Actions, "edit_project_draft", false);
        Allowed((await Body<TeamWorkflowActionsDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}/actions"))).Actions,
            "invite_member", false, "ROSTER_LOCKED");
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(project.Id))!.Status = "APPROVED";
            db.ProjectPeriods.Add(new ProjectPeriod { AcademicSemesterId = s.SemesterId, Code = "SUP", Name = "Supervisor selection",
                PeriodType = "SUPERVISOR_SELECTION", Status = "ACTIVE", StartAt = TeamDatabaseFixture.Now.AddDays(-1),
                EndAt = TeamDatabaseFixture.Now.AddDays(1), MaxProjectsPerSupervisor = 3 });
            await db.SaveChangesAsync();
        }
        actions = await Body<ProjectWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(actions.Actions, "view_supervisor_candidates", true);
        Allowed(actions.Actions, "send_supervisor_request", true);
        Allowed((await Body<ProjectWorkflowActionsDto>(await member.GetAsync(url))).Actions, "send_supervisor_request", false);
        await using (var db = database.CreateContext())
            await db.ProjectPeriods.Where(p => p.AcademicSemesterId == s.SemesterId && p.PeriodType == "SUPERVISOR_SELECTION")
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.EndAt, TeamDatabaseFixture.Now));
        actions = await Body<ProjectWorkflowActionsDto>(await leader.GetAsync(url));
        Allowed(actions.Actions, "send_supervisor_request", false, "SUPERVISOR_SELECTION_POLICY_UNAVAILABLE");
    }

    [Theory]
    [InlineData("/api/v1/auth/me/context?academicSemesterId=0")]
    [InlineData("/api/v1/teams/0/actions")]
    [InlineData("/api/v1/projects/-1/actions")]
    public async Task Invalid_ids_and_anonymous_requests_are_rejected(string url)
    {
        var s = await database.SeedAsync(); using var app = new TeamTestFactory(database, s);
        using var anonymous = app.CreateClient(); using var client = app.CreateAuthenticatedClient(s.Students[0]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Inactive_accounts_cannot_read_context_even_with_valid_claims()
    {
        var s = await database.SeedAsync(); using var app = new TeamTestFactory(database, s);
        using var client = app.CreateAuthenticatedClient(s.Students[0]);
        await using (var db = database.CreateContext())
            await db.Users.Where(u => u.Id == s.Students[0]).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, "INACTIVE"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ContextUrl)).StatusCode);
    }
}
