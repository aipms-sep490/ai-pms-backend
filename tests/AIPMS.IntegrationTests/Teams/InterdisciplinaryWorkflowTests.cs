using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed class InterdisciplinaryWorkflowTests(TeamDatabaseFixture database) : IClassFixture<TeamDatabaseFixture>
{
    private sealed record Scenario(TeamScenario Team, long LeadDepartment, long OtherDepartment, long LeadStaff, long OtherStaff);

    private async System.Threading.Tasks.Task<Scenario> SeedAsync()
    {
        var s = await database.SeedAsync();
        await using var db = database.CreateContext();
        var major = await db.Majors.Include(m => m.Department).SingleAsync(m => m.Id == s.IsMajorId);
        var lead = major.DepartmentId;
        var department = new Department { Code = "BUS", Name = "Business", OrganizationId = major.Department.OrganizationId, IsActive = true };
        db.Add(department);
        await db.SaveChangesAsync();
        major.DepartmentId = department.Id;
        foreach (var user in await db.Users.Where(u => u.MajorId == s.IsMajorId).ToListAsync()) user.DepartmentId = department.Id;
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Code == "DEPARTMENT_STAFF");
        if (role is null) { role = new Role { Code = "DEPARTMENT_STAFF", Name = "Staff", IsSystemRole = true }; db.Add(role); }
        User Staff(long dept) => new() { Email = Guid.NewGuid() + "@example.test", FullName = "Staff", PasswordHash = "unused-test-hash",
            Status = "ACTIVE", DepartmentId = dept, UserRoleUsers = new List<UserRole> { new() { Role = role } } };
        var a = Staff(lead); var b = Staff(department.Id); db.AddRange(a, b);
        (await db.ProjectPeriods.FindAsync(s.PeriodId))!.MinDistinctMajors = 2;
        await db.SaveChangesAsync();
        return new(s, lead, department.Id, a.Id, b.Id);
    }

    private static async System.Threading.Tasks.Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static TeamAcademicScopeRequest Scope(Scenario s, Guid? token = null) => new("INTERDISCIPLINARY", null,
        s.LeadDepartment, [new(s.Team.SeMajorId, 1, 2, "Software"), new(s.Team.IsMajorId, 1, 1, "Business analysis")], token);

    private static async System.Threading.Tasks.Task<TeamDto> Create(HttpClient leader, Scenario s) =>
        await Body<TeamDto>(await leader.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = s.Team.SemesterId,
            code = "HYBRID", name = "Hybrid capstone", academicScope = Scope(s) }));

    private static async System.Threading.Tasks.Task<TeamInvitationDto> Invite(HttpClient leader, long teamId, long userId) =>
        await Body<TeamInvitationDto>(await leader.PostAsJsonAsync($"/api/v1/teams/{teamId}/invitations", new { invitedUserId = userId }));

    private static async System.Threading.Tasks.Task<ProjectDto> Proposal(HttpClient leader, Scenario s) =>
        await Body<ProjectDto>(await leader.PostAsJsonAsync("/api/v1/projects", new CreateProjectDraftRequest(
            "Hybrid proposal", "Description", "Objectives", "Problem", "Expected output",
            [s.Team.SeMajorId, s.Team.IsMajorId], "Education", ["Dotnet"], ["Capstone"])));

    private static async System.Threading.Tasks.Task<ProjectDto> Transition(HttpClient client, ProjectDto project, string action) =>
        await Body<ProjectDto>(await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/{action}",
            new { concurrencyToken = project.ConcurrencyToken, reason = "Please revise the scope" }));

    private static async System.Threading.Tasks.Task<ProjectAcademicReviewDto> Review(HttpClient client, long id) =>
        await Body<ProjectAcademicReviewDto>(await client.GetAsync($"/api/v1/projects/{id}/academic-review"));

    private static async System.Threading.Tasks.Task<ProjectAcademicReviewDto> Decide(HttpClient client, long id,
        ProjectAcademicReviewDto review, string decision = "APPROVED") =>
        await Body<ProjectAcademicReviewDto>(await client.PostAsJsonAsync($"/api/v1/projects/{id}/department-decisions",
            new DepartmentDecisionRequest(review.LatestSubmission!.Id, review.ConcurrencyToken, decision, "Reviewed requirements")));

    [Fact]
    public async Task Cross_department_journey_requires_all_decisions_and_locks_roster()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s);
        Assert.False(team.Eligibility.CanRegister);
        Assert.Contains($"MAJOR_MIN_MEMBERS:{s.Team.IsMajorId}", team.Eligibility.Reasons);
        var candidates = await Body<PagedResult<TeamInvitationCandidateDto>>(await leader.GetAsync($"/api/v1/teams/{team.Id}/invitation-candidates"));
        Assert.Contains(candidates.Items, u => u.UserId == s.Team.Students[4]);
        var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        team = await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        Assert.True(team.Eligibility.CanRegister);
        var project = await Transition(leader, await Proposal(leader, s), "submit");
        Assert.Equal("SUBMITTED", project.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", Scope(s, team.AcademicScope!.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsync($"/api/v1/teams/{team.Id}/leave", null)).StatusCode);
        project = await Transition(lead, project, "start-review");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/v1/projects/{project.Id}/approve", new { concurrencyToken = project.ConcurrencyToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await lead.PostAsJsonAsync($"/api/v1/projects/{project.Id}/approve", new { concurrencyToken = project.ConcurrencyToken })).StatusCode);
        var review = await Review(leader, project.Id);
        Assert.Equal(2, review.LatestSubmission!.Decisions.Count);
        Assert.Equal(2, review.LatestSubmission.Evidence.Members.Count);
        review = await Decide(other, project.Id, review);
        review = await Decide(lead, project.Id, review);
        project = await Transition(lead, project with { ConcurrencyToken = review.ConcurrencyToken }, "approve");
        Assert.Equal("APPROVED", project.Status);
        Assert.Equal("INTERDISCIPLINARY", project.AcademicScope!.ProjectMode);
    }

    [Fact]
    public async Task Concurrent_accepts_respect_per_major_maximum_even_with_total_capacity_left()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var a = app.CreateAuthenticatedClient(s.Team.Students[4]); using var b = app.CreateAuthenticatedClient(s.Team.Students[5]);
        var team = await Create(leader, s);
        var first = await Invite(leader, team.Id, s.Team.Students[4]); var second = await Invite(leader, team.Id, s.Team.Students[5]);
        var responses = await Task.WhenAll(a.PostAsync($"/api/v1/teams/invitations/{first.Id}/accept", null),
            b.PostAsync($"/api/v1/teams/invitations/{second.Id}/accept", null));
        Assert.Single(responses, r => r.IsSuccessStatusCode);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        team = await Body<TeamDto>(await leader.GetAsync($"/api/v1/teams/{team.Id}"));
        Assert.Equal(2, team.Members.Count);
        var candidates = await Body<PagedResult<TeamInvitationCandidateDto>>(await leader.GetAsync($"/api/v1/teams/{team.Id}/invitation-candidates"));
        Assert.DoesNotContain(candidates.Items, c => c.MajorId == s.Team.IsMajorId);
    }

    [Fact]
    public async Task Revision_preserves_old_evidence_and_requires_new_department_decisions()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Transition(lead, await Transition(leader, await Proposal(leader, s), "submit"), "start-review");
        var review = await Decide(other, project.Id, await Review(leader, project.Id), "REJECTED");
        Assert.Equal(HttpStatusCode.Conflict, (await lead.PostAsJsonAsync($"/api/v1/projects/{project.Id}/approve", new { concurrencyToken = review.ConcurrencyToken })).StatusCode);
        project = await Transition(lead, project with { ConcurrencyToken = review.ConcurrencyToken }, "revision");
        project = await Body<ProjectDto>(await leader.PutAsJsonAsync($"/api/v1/projects/{project.Id}", new UpdateProjectDraftRequest(
            project.ConcurrencyToken, "Revised proposal", "Description", "Objectives", "Problem", "New output",
            [s.Team.SeMajorId, s.Team.IsMajorId], "Education", ["Dotnet"], ["Capstone"])));
        project = await Transition(leader, project, "resubmit");
        project = await Transition(lead, project, "start-review");
        var next = await Review(leader, project.Id);
        Assert.NotEqual(review.LatestSubmission!.Id, next.LatestSubmission!.Id);
        Assert.All(next.LatestSubmission.Decisions, d => Assert.Equal("PENDING", d.Decision));
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions",
            new DepartmentDecisionRequest(review.LatestSubmission.Id, next.ConcurrencyToken, "APPROVED", null))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("REJECTED", (await db.Set<ProjectDepartmentDecision>().SingleAsync(d => d.SnapshotId == review.LatestSubmission.Id && d.DepartmentId == s.OtherDepartment)).Decision);
        Assert.Equal(2, await db.Set<ProjectRegistrationSnapshot>().CountAsync(x => x.ProjectId == project.Id));
    }

    [Theory]
    [InlineData("PROJECT_SUBMITTED", "submit")]
    [InlineData("PROJECT_REVIEW_STARTED", "start-review")]
    [InlineData("PROJECT_DEPARTMENT_DECISION", "decision")]
    [InlineData("PROJECT_APPROVED", "approve")]
    public async Task Audit_failure_rolls_back_submission_review_and_decisions(string auditAction, string operation)
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        if (operation != "submit") project = await Transition(leader, project, "submit");
        if (operation is "decision" or "approve") project = await Transition(lead, project, "start-review");
        var before = await Review(leader, project.Id);
        if (operation == "approve")
        {
            before = await Decide(other, project.Id, before); before = await Decide(lead, project.Id, before);
        }
        using var failing = new TeamTestFactory(database, s.Team, failAuditAction: auditAction);
        using var client = failing.CreateAuthenticatedClient(operation == "submit" ? s.Team.Students[0] : s.LeadStaff,
            roles: operation == "submit" ? ["STUDENT"] : ["DEPARTMENT_STAFF"]);
        var response = operation == "decision"
            ? await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions", new DepartmentDecisionRequest(before.LatestSubmission!.Id, before.ConcurrencyToken, "APPROVED", null))
            : await client.PostAsJsonAsync($"/api/v1/projects/{project.Id}/{operation}", new { concurrencyToken = before.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var after = await Review(leader, project.Id);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(before.LatestSubmission?.Id, after.LatestSubmission?.Id);
        if (operation == "decision") Assert.All(after.LatestSubmission!.Decisions, d => Assert.Equal("PENDING", d.Decision));
    }

    [Fact]
    public async Task Scope_rejects_stale_tokens_nonleaders_foreign_majors_and_roster_exclusion()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var outsider = app.CreateAuthenticatedClient(s.Team.Students[1]);
        var team = await Create(leader, s); var scope = Scope(s, team.AcademicScope!.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", scope)).StatusCode);
        team = await Body<TeamDto>(await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", scope));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", scope)).StatusCode);
        var foreign = await database.SeedAsync();
        scope = Scope(s, team.AcademicScope!.ConcurrencyToken) with { Requirements = [new(s.Team.SeMajorId, 1, 2, "Software"), new(foreign.IsMajorId, 1, 1, "Analysis")] };
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", scope)).StatusCode);
        scope = Scope(s, team.AcademicScope.ConcurrencyToken) with { ProjectMode = "SINGLE_MAJOR", PrimaryMajorId = s.Team.IsMajorId,
            LeadDepartmentId = s.OtherDepartment, Requirements = [new(s.Team.IsMajorId, 1, 3, "Analysis")] };
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", scope)).StatusCode);
    }

    [Fact]
    public async Task Submission_rechecks_policy_and_major_metadata_and_scope_changes_invalidate_project_token()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Proposal(leader, s);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync($"/api/v1/projects/{project.Id}/majors",
            new SetProjectMajorsRequest(project.ConcurrencyToken, [s.Team.SeMajorId]))).StatusCode);
        team = await Body<TeamDto>(await leader.PutAsJsonAsync($"/api/v1/teams/{team.Id}/academic-scope", Scope(s, team.AcademicScope!.ConcurrencyToken)));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/submit", new { concurrencyToken = project.ConcurrencyToken })).StatusCode);
        project = await Body<ProjectDto>(await leader.GetAsync($"/api/v1/projects/{project.Id}"));
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(s.Team.PeriodId))!.MinTeamSize = 3;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/projects/{project.Id}/submit", new { concurrencyToken = project.ConcurrencyToken })).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal("DRAFT", (await verify.Projects.FindAsync(project.Id))!.Status);
        Assert.False(await verify.Set<ProjectRegistrationSnapshot>().AnyAsync(x => x.ProjectId == project.Id));
    }

    [Fact]
    public async Task Academic_review_and_lists_enforce_membership_and_department_scope()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var outsider = app.CreateAuthenticatedClient(s.Team.Students[2]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Transition(lead, await Transition(leader, await Proposal(leader, s), "submit"), "start-review");
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/projects/{project.Id}/academic-review")).StatusCode);
        Assert.Empty((await Body<PagedResult<ProjectSummaryDto>>(await outsider.GetAsync($"/api/v1/projects?teamId={team.Id}"))).Items);
        Assert.Single((await Body<PagedResult<ProjectSummaryDto>>(await member.GetAsync($"/api/v1/projects?teamId={team.Id}"))).Items);
        var review = await Review(leader, project.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions",
            new DepartmentDecisionRequest(review.LatestSubmission!.Id, review.ConcurrencyToken, "APPROVED", null))).StatusCode);
        // A staff claim without an authoritative staff role does not grant a department decision.
        using var forgedStaff = app.CreateAuthenticatedClient(s.Team.Students[0], roles: ["DEPARTMENT_STAFF"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await forgedStaff.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions",
            new DepartmentDecisionRequest(review.LatestSubmission.Id, review.ConcurrencyToken, "APPROVED", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lead.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions",
            new DepartmentDecisionRequest(review.LatestSubmission.Id, review.ConcurrencyToken, "REJECTED", "  "))).StatusCode);
    }

    [Fact]
    public async Task Concurrent_department_decisions_require_refresh_and_cannot_skip_a_department()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Transition(lead, await Transition(leader, await Proposal(leader, s), "submit"), "start-review");
        var review = await Review(leader, project.Id);
        var request = new DepartmentDecisionRequest(review.LatestSubmission!.Id, review.ConcurrencyToken, "APPROVED", null);
        var responses = await Task.WhenAll(lead.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions", request),
            other.PostAsJsonAsync($"/api/v1/projects/{project.Id}/department-decisions", request));
        Assert.Single(responses, r => r.IsSuccessStatusCode); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        review = await Review(leader, project.Id);
        Assert.Single(review.LatestSubmission!.Decisions, d => d.Decision == "APPROVED");
        Assert.Equal(HttpStatusCode.Conflict, (await lead.PostAsJsonAsync($"/api/v1/projects/{project.Id}/approve", new { concurrencyToken = review.ConcurrencyToken })).StatusCode);
        var pending = review.LatestSubmission.Decisions.Single(d => d.Decision == "PENDING");
        await Decide(pending.DepartmentId == s.LeadDepartment ? lead : other, project.Id, review);
        review = await Review(leader, project.Id);
        Assert.Equal("APPROVED", (await Transition(lead, project with { ConcurrencyToken = review.ConcurrencyToken }, "approve")).Status);
    }

    [Fact]
    public async Task Submitted_department_scope_survives_later_major_reassignment_and_migration_rerun()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        using var other = app.CreateAuthenticatedClient(s.OtherStaff, roles: ["DEPARTMENT_STAFF"]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Transition(lead, await Transition(leader, await Proposal(leader, s), "submit"), "start-review");
        var before = await Review(other, project.Id);
        await using (var db = database.CreateContext())
        {
            (await db.Majors.FindAsync(s.Team.IsMajorId))!.DepartmentId = s.LeadDepartment;
            await db.SaveChangesAsync();
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "db", "changes"))) directory = directory.Parent;
            var sql = await System.IO.File.ReadAllTextAsync(Path.Combine(directory!.FullName, "db", "changes", "20260912_add_interdisciplinary_projects.sql"));
            await db.Database.ExecuteSqlRawAsync(sql);
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        var after = await Review(other, project.Id);
        Assert.Equal(before.ConcurrencyToken, after.ConcurrencyToken);
        Assert.Equal(before.LatestSubmission!.Id, after.LatestSubmission!.Id);
        Assert.Contains(s.OtherDepartment, after.LatestSubmission.Evidence.DepartmentIds);
        Assert.Single((await Body<PagedResult<ProjectSummaryDto>>(await other.GetAsync($"/api/v1/projects?teamId={team.Id}"))).Items);
        var queue = await Body<PagedResult<ProjectSummaryDto>>(await other.GetAsync("/api/v1/projects/review-queue"));
        Assert.Contains(queue.Items, p => p.Id == project.Id);
        await Decide(other, project.Id, after);
    }

    [Fact]
    public async Task Explicit_single_major_uses_primary_major_and_lead_review_without_extra_decisions()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[1]);
        using var lead = app.CreateAuthenticatedClient(s.LeadStaff, roles: ["DEPARTMENT_STAFF"]);
        var scope = new TeamAcademicScopeRequest("SINGLE_MAJOR", s.Team.SeMajorId, s.LeadDepartment,
            [new(s.Team.SeMajorId, 2, 3, "Software")]);
        var team = await Body<TeamDto>(await leader.PostAsJsonAsync("/api/v1/teams", new { academicSemesterId = s.Team.SemesterId,
            code = "SINGLE", name = "Single major", academicScope = scope }));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync($"/api/v1/teams/{team.Id}/invitations", new { invitedUserId = s.Team.Students[4] })).StatusCode);
        var invitation = await Invite(leader, team.Id, s.Team.Students[1]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var project = await Body<ProjectDto>(await leader.PostAsJsonAsync("/api/v1/projects", new CreateProjectDraftRequest(
            "Single proposal", "Description", "Objectives", "Problem", "Expected output", [s.Team.SeMajorId], "Education", ["Dotnet"], ["Capstone"])));
        project = await Transition(lead, await Transition(leader, project, "submit"), "start-review");
        Assert.Empty((await Review(leader, project.Id)).LatestSubmission!.Decisions);
        Assert.Equal("APPROVED", (await Transition(lead, project, "approve")).Status);
    }
}
