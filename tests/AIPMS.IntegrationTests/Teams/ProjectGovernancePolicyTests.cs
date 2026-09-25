using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Teams;

public sealed partial class InterdisciplinaryWorkflowTests
{
    private async Task Policy(Scenario s, string modes, string sources)
    {
        await using var db = database.CreateContext();
        var repository = new SemesterRepository(db);
        await repository.ExecuteInTransactionAsync(() => repository.SetProjectPeriodGovernanceAsync(s.Team.PeriodId, modes, sources), default);
    }

    [Fact]
    public async Task Disabled_mode_blocks_team_creation_and_disabled_source_blocks_proposals_and_submission()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]);
        using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        await Policy(s, "SINGLE_MAJOR", "STUDENT_PROPOSAL");
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync("/api/v1/teams", new {
            academicSemesterId = s.Team.SemesterId, code = "HYBRID", name = "Hybrid capstone", academicScope = Scope(s) })).StatusCode);
        await Policy(s, "INTERDISCIPLINARY", "STUDENT_PROPOSAL");
        var team = await Create(leader, s);
        var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var draft = await Proposal(leader, s);
        await Policy(s, "INTERDISCIPLINARY", "PUBLISHED_TOPIC");
        var blocked = await leader.PostAsJsonAsync($"/api/v1/projects/{draft.Id}/submit", new { concurrencyToken = draft.ConcurrencyToken });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("PROPOSAL_SOURCE_NOT_ALLOWED", await blocked.Content.ReadAsStringAsync());
        await using var check = database.CreateContext();
        Assert.Equal("DRAFT", (await check.Projects.FindAsync(draft.Id))!.Status);
        Assert.False(await check.Set<ProjectRegistrationSnapshot>().AnyAsync(x => x.ProjectId == draft.Id));
    }

    [Fact]
    public async Task Published_topic_only_period_supports_atomic_creation_and_policy_snapshot_survives_later_edits()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        await Policy(s, "INTERDISCIPLINARY", "PUBLISHED_TOPIC");
        long topicId; int version;
        await using (var db = database.CreateContext())
        {
            version = (await db.ProjectPeriods.FindAsync(s.Team.PeriodId))!.PolicyVersion;
            var topic = new ProjectTopic { ProjectPeriodId = s.Team.PeriodId, LeadDepartmentId = s.LeadDepartment,
                ProjectMode = "INTERDISCIPLINARY", Code = "CATALOG", Title = "Hybrid topic", Status = "PUBLISHED",
                CreatedBy = s.LeadStaff, UpdatedBy = s.LeadStaff, PublishedBy = s.LeadStaff, PublishedAt = DateTime.UtcNow,
                ConcurrencyToken = Guid.NewGuid(), Requirements = [
                    new() { MajorId = s.Team.SeMajorId, DepartmentId = s.LeadDepartment, MinMembers = 1, MaxMembers = 2, Responsibility = "Software" },
                    new() { MajorId = s.Team.IsMajorId, DepartmentId = s.OtherDepartment, MinMembers = 1, MaxMembers = 1, Responsibility = "Business" }] };
            db.Add(topic); await db.SaveChangesAsync(); topicId = topic.Id;
        }
        var input = new CreateProjectDraftRequest("Proposal", "Description", "Objectives", "Problem", "Output",
            [s.Team.SeMajorId, s.Team.IsMajorId], "Education", ["Dotnet"], ["Capstone"]);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync("/api/v1/projects", input)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await leader.PostAsJsonAsync("/api/v1/projects", input with { TopicId = long.MaxValue })).StatusCode);
        var draft = await Body<ProjectDto>(await leader.PostAsJsonAsync("/api/v1/projects", input with { TopicId = topicId }));
        Assert.Equal("PUBLISHED_TOPIC", draft.ProposalSource); Assert.Equal(topicId, draft.TopicId);
        var submitted = await Transition(leader, draft, "submit");
        await Policy(s, "SINGLE_MAJOR,INTERDISCIPLINARY", "STUDENT_PROPOSAL");
        var review = await Review(leader, submitted.Id);
        Assert.Equal(version, review.LatestSubmission!.Evidence.PolicyVersion);
        Assert.Equal("PUBLISHED_TOPIC", review.LatestSubmission.Evidence.AllowedProposalSources);
        Assert.Equal("PUBLISHED_TOPIC", review.LatestSubmission.Evidence.ProposalSource);
        await using var check = database.CreateContext();
        Assert.Single(await check.Projects.Where(p => p.TeamId == team.Id).ToListAsync());
        Assert.True((await check.ProjectPeriods.FindAsync(s.Team.PeriodId))!.PolicyVersion > version);
    }

    [Fact]
    public async Task Equivalent_policy_does_not_increment_version_and_closed_period_rejects_changes()
    {
        var s = await SeedAsync();
        await Policy(s, "INTERDISCIPLINARY", "STUDENT_PROPOSAL");
        int version;
        await using (var db = database.CreateContext()) version = (await db.ProjectPeriods.FindAsync(s.Team.PeriodId))!.PolicyVersion;
        await Policy(s, " interdisciplinary,INTERDISCIPLINARY ", " student_proposal ");
        await using (var db = database.CreateContext())
        {
            var period = (await db.ProjectPeriods.FindAsync(s.Team.PeriodId))!;
            Assert.Equal(version, period.PolicyVersion); period.Status = "CLOSED"; await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<AIPMS.Application.Common.Exceptions.ConflictException>(() => Policy(s, "SINGLE_MAJOR", "PUBLISHED_TOPIC"));
    }

    [Fact]
    public async Task Snapshot_remains_authoritative_for_workspace_access_after_live_major_department_changes()
    {
        var s = await SeedAsync(); using var app = new TeamTestFactory(database, s.Team);
        using var leader = app.CreateAuthenticatedClient(s.Team.Students[0]); using var member = app.CreateAuthenticatedClient(s.Team.Students[4]);
        var team = await Create(leader, s); var invitation = await Invite(leader, team.Id, s.Team.Students[4]);
        await Body<TeamDto>(await member.PostAsync($"/api/v1/teams/invitations/{invitation.Id}/accept", null));
        var submitted = await Transition(leader, await Proposal(leader, s), "submit");
        long outsideStaff;
        await using (var db = database.CreateContext())
        {
            var department = new AIPMS.Infrastructure.Persistence.Generated.Models.Department {
                Code = Guid.NewGuid().ToString("N"), Name = "Outside", IsActive = true,
                OrganizationId = (await db.Departments.FindAsync(s.LeadDepartment))!.OrganizationId };
            var staff = new AIPMS.Infrastructure.Persistence.Generated.Models.User { Email = Guid.NewGuid() + "@test.local", FullName = "Outside staff",
                PasswordHash = "unused", Status = "ACTIVE", Department = department, UserRoleUsers = [new() {
                    RoleId = await db.Roles.Where(r => r.Code == "DEPARTMENT_STAFF").Select(r => r.Id).SingleAsync() }] };
            db.Users.Add(staff); await db.SaveChangesAsync(); outsideStaff = staff.Id;
            (await db.Majors.FindAsync(s.Team.IsMajorId))!.DepartmentId = department.Id;
            await db.SaveChangesAsync();
            var access = new AIPMS.Infrastructure.Services.Projects.ProjectAccessService(db);
            Assert.True(await access.CanAccessAsync(s.OtherStaff, submitted.Id));
            Assert.False(await access.CanAccessAsync(outsideStaff, submitted.Id));
        }
        using var outsider = app.CreateAuthenticatedClient(outsideStaff, roles: ["DEPARTMENT_STAFF"]);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/v1/projects/{submitted.Id}/academic-review")).StatusCode);
        var review = await Review(leader, submitted.Id);
        Assert.Equal(s.OtherDepartment, review.LatestSubmission!.Evidence.MajorDepartmentIds![s.Team.IsMajorId]);
    }
}
