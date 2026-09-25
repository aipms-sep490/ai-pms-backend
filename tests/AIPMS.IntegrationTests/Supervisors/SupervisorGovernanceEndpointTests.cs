using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests
{
    private async Task<(RequestProject Project, long FirstMajor, long SecondMajor, long Mentor)> MentorProject(SupervisorScenario s)
    {
        var p = await SeedProject(s);
        await SeedActivationTemplate(s.Admin, p.SemesterId);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var primary = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        await Body<SupervisorRequestDto>(await Respond(lecturer, primary.Id, "accept"));
        var profileId = await AddProfile(s.NewLecturer);
        await using var db = database.CreateContext();
        var majorId = await db.ProjectMajors.Where(m => m.ProjectId == p.Id).Select(m => m.MajorId).SingleAsync();
        var second = new Major { DepartmentId = s.DepartmentId, Code = Guid.NewGuid().ToString("N"), Name = "AI", IsActive = true };
        db.ProjectMajors.Add(new() { ProjectId = p.Id, Major = second });
        db.SupervisorExpertises.AddRange(new SupervisorExpertise { SupervisorProfileId = profileId, ExpertiseName = "SE" },
            new SupervisorExpertise { SupervisorProfileId = profileId, ExpertiseName = "AI" });
        (await db.SupervisorProfiles.FindAsync(profileId))!.MaxActiveProjects = 1;
        // Mentor assignment remains possible during execution after initial supervisor selection closes.
        (await db.ProjectPeriods.FindAsync(p.PeriodId))!.Status = "CLOSED";
        await db.SaveChangesAsync();
        return (p, majorId, second.Id, profileId);
    }

    private static Task<HttpResponseMessage> SendMentor(HttpClient leader, long project, long profile, long major) =>
        leader.PostAsJsonAsync(ProjectUrl(project), new { supervisorProfileId = profile, assignmentType = "DISCIPLINE_MENTOR", majorId = major });

    [Fact]
    public async Task Swagger_keeps_existing_routes_and_exposes_governance_contracts()
    {
        using var app = new SupervisorFactory(database, clock: new Clock()); using var client = app.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var route in new[] { "/api/v1/projects/{projectId}/supervisor-candidates", "/api/v1/projects/{projectId}/supervisor-requests",
            "/api/v1/supervisor-assignments/{assignmentId}/replace", "/api/v1/supervisor-assignments/{assignmentId}/end",
            "/api/v1/projects/{id}/academic-review", "/api/v1/projects/{id}/department-decisions" })
            Assert.True(paths.TryGetProperty(route, out _), route);
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.GetProperty("SendSupervisorRequest").GetProperty("properties").TryGetProperty("assignmentType", out _));
        Assert.True(schemas.GetProperty("SupervisorAssignmentDto").GetProperty("properties").TryGetProperty("replacesAssignmentId", out _));
        Assert.True(schemas.GetProperty("ProjectPeriodDto").GetProperty("properties").TryGetProperty("policyVersion", out _));
    }

    [Fact]
    public async Task Legacy_project_review_requires_persisted_staff_in_the_responsible_department()
    {
        var s = await database.SeedAsync(); var p = await SeedProject(s);
        string version;
        await using (var db = database.CreateContext())
        {
            var project = (await db.Projects.FindAsync(p.Id))!; project.Status = "SUBMITTED";
            await db.SaveChangesAsync(); version = Convert.ToBase64String(project.RowVersion);
        }
        using var app = new SupervisorFactory(database, clock: new Clock());
        var url = $"/api/v1/projects/{p.Id}/start-review";
        foreach (var user in new[] { s.Admin, s.OutsideStaff, p.LeaderId })
        {
            using var denied = app.CreateAuthenticatedClient(user, roles: [user == s.Admin ? "ADMIN" : "DEPARTMENT_STAFF"]);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync(url, new { concurrencyToken = version })).StatusCode);
        }
        using var staff = app.CreateAuthenticatedClient(s.Staff, roles: ["DEPARTMENT_STAFF"]);
        var response = await staff.PostAsJsonAsync(url, new { concurrencyToken = version });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await using var check = database.CreateContext();
        Assert.Equal("UNDER_REVIEW", (await check.Projects.FindAsync(p.Id))!.Status);
    }

    [Fact]
    public async Task Mentor_slots_are_independent_preserve_primary_and_activate_milestones_only_once()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(m.Project.LeaderId);
        using var mentor = app.CreateAuthenticatedClient(s.NewLecturer);
        var first = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        var second = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.SecondMajor));
        Assert.Equal(HttpStatusCode.Conflict, (await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor)).StatusCode);
        await Body<SupervisorRequestDto>(await Respond(mentor, first.Id, "accept"));
        await using (var db = database.CreateContext())
            Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(second.Id))!.Status);
        var accepted = await Body<SupervisorRequestDto>(await Respond(mentor, second.Id, "accept"));
        Assert.Equal(accepted, await Body<SupervisorRequestDto>(await Respond(mentor, second.Id, "accept")));
        Assert.Equal(HttpStatusCode.OK, (await mentor.GetAsync($"/api/v1/milestones/project/{m.Project.Id}")).StatusCode);
        var history = await Body<PagedResult<SupervisorAssignmentDto>>(await leader.GetAsync($"/api/v1/projects/{m.Project.Id}/supervisor-assignments"));
        Assert.Equal(3, history.Items.Count);
        Assert.Single(history.Items, a => a.IsPrimary && a.SupervisorProfileId == s.ProfileId);
        Assert.All(history.Items.Where(a => !a.IsPrimary), a => Assert.Equal(s.NewLecturer, a.AssignedBy));
        await using var check = database.CreateContext();
        Assert.Equal("ACTIVE", (await check.Projects.FindAsync(m.Project.Id))!.Status);
        Assert.Equal(2, await check.ProjectStatusHistories.CountAsync(h => h.ProjectId == m.Project.Id));
        Assert.Equal(1, await check.Milestones.CountAsync(x => x.ProjectId == m.Project.Id));
        Assert.Equal(3, await check.AuditLogs.CountAsync(x => x.Action == "SUPERVISOR_ASSIGNED"
            && history.Items.Select(a => a.Id.ToString()).Contains(x.EntityId!)));
    }

    [Fact]
    public async Task Mentor_candidates_send_and_accept_enforce_major_expertise_scope_and_capacity()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        var outside = await AddProfile(s.OtherLecturer);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(m.Project.LeaderId);
        using var mentor = app.CreateAuthenticatedClient(s.NewLecturer);
        var candidates = await Body<PagedResult<SupervisorCandidateDto>>(await leader.GetAsync(
            $"/api/v1/projects/{m.Project.Id}/supervisor-candidates?assignmentType=DISCIPLINE_MENTOR&majorId={m.FirstMajor}"));
        Assert.Equal(m.Mentor, Assert.Single(candidates.Items).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await SendMentor(leader, m.Project.Id, outside, m.FirstMajor)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendMentor(leader, m.Project.Id, s.ProfileId, m.FirstMajor)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await SendMentor(leader, m.Project.Id, m.Mentor, long.MaxValue)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync($"/api/v1/projects/{m.Project.Id}/supervisor-candidates?majorId={m.FirstMajor}")).StatusCode);
        var request = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorProfiles.FindAsync(m.Mentor))!.MaxActiveProjects = 0;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Respond(mentor, request.Id, "accept")).StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal("PENDING", (await check.SupervisorRequests.FindAsync(request.Id))!.Status);
        Assert.Single(await check.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id).ToListAsync());
    }

    [Fact]
    public async Task Replacement_is_staff_scoped_atomic_replay_safe_and_revokes_old_workspace_access()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        long id;
        await using (var db = database.CreateContext())
            id = await db.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id && a.IsPrimary).Select(a => a.Id).SingleAsync();
        var url = $"/api/v1/supervisor-assignments/{id}/replace";
        var input = new { supervisorProfileId = m.Mentor, reason = " Workload reassignment " };
        foreach (var actor in new[] { s.Admin, s.OutsideStaff, m.Project.LeaderId, s.Lecturer })
        {
            using var denied = app.CreateAuthenticatedClient(actor);
            Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsJsonAsync(url, input)).StatusCode);
        }
        using var staff = app.CreateAuthenticatedClient(s.Staff);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(url, new { supervisorProfileId = m.Mentor, reason = " " })).StatusCode);
        var replacement = await Body<SupervisorAssignmentDto>(await staff.PostAsJsonAsync(url, input));
        Assert.True(replacement.IsPrimary); Assert.Equal(id, replacement.ReplacesAssignmentId); Assert.Equal(s.Staff, replacement.AssignedBy);
        Assert.Equal(replacement, await Body<SupervisorAssignmentDto>(await staff.PostAsJsonAsync(url, input)));
        using var oldLecturer = app.CreateAuthenticatedClient(s.Lecturer);
        using var newLecturer = app.CreateAuthenticatedClient(s.NewLecturer);
        Assert.Equal(HttpStatusCode.Forbidden, (await oldLecturer.GetAsync($"/api/v1/milestones/project/{m.Project.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await newLecturer.GetAsync($"/api/v1/milestones/project/{m.Project.Id}")).StatusCode);
        var previous = await Body<SupervisorAssignmentDto>(await oldLecturer.GetAsync($"/api/v1/supervisor-assignments/{id}"));
        Assert.Equal(s.Staff, previous.EndedBy); Assert.Equal("Workload reassignment", previous.EndReason);
        await using var check = database.CreateContext();
        Assert.Equal("ACTIVE", (await check.Projects.FindAsync(m.Project.Id))!.Status);
        Assert.Single(await check.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id && a.IsPrimary && a.EndedAt == null).ToListAsync());
        Assert.Equal(1, await check.Milestones.CountAsync(x => x.ProjectId == m.Project.Id));
        var notice = await check.Notifications.Include(n => n.NotificationRecipients)
            .SingleAsync(n => n.NotificationType == "SUPERVISOR_REPLACED" && n.RelatedEntityId == replacement.Id);
        Assert.Contains(notice.NotificationRecipients, r => r.UserId == s.Lecturer);
        Assert.Contains(notice.NotificationRecipients, r => r.UserId == s.NewLecturer);
        Assert.Contains(notice.NotificationRecipients, r => r.UserId == m.Project.LeaderId);
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_REPLACED" && a.EntityId == replacement.Id.ToString()));
    }

    [Fact]
    public async Task Replacement_audit_failure_rolls_back_history_requests_assignments_and_notifications()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        long id;
        await using (var db = database.CreateContext())
            id = await db.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id && a.IsPrimary).Select(a => a.Id).SingleAsync();
        using var app = new SupervisorFactory(database, saveInterceptor: new FailReplacementAudit(), clock: new Clock());
        using var staff = app.CreateAuthenticatedClient(s.Staff);
        Assert.Equal(HttpStatusCode.InternalServerError, (await staff.PostAsJsonAsync($"/api/v1/supervisor-assignments/{id}/replace",
            new { supervisorProfileId = m.Mentor, reason = "Reassign" })).StatusCode);
        await using var check = database.CreateContext();
        Assert.Null((await check.SupervisorAssignments.FindAsync(id))!.EndedAt);
        Assert.Single(await check.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id).ToListAsync());
        Assert.Single(await check.SupervisorRequests.Where(a => a.ProjectId == m.Project.Id).ToListAsync());
        Assert.False(await check.Notifications.AnyAsync(n => n.NotificationType == "SUPERVISOR_REPLACED" && n.RelatedEntityId == id));
    }

    private sealed class FailReplacementAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(e => e.State == EntityState.Added && e.Entity.Action == "SUPERVISOR_REPLACED"))
                throw new InvalidOperationException("Simulated replacement audit failure.");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Concurrent_mentor_acceptances_have_one_winner_for_the_same_major()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        await using (var db = database.CreateContext())
        {
            db.SupervisorExpertises.Add(new() { SupervisorProfileId = s.ProfileId, ExpertiseName = "SE" });
            await db.SaveChangesAsync();
        }
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var leader = setup.CreateAuthenticatedClient(m.Project.LeaderId);
        var a = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        var b = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, s.ProfileId, m.FirstMajor));
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_profiles"), clock: new Clock());
        using var first = app.CreateAuthenticatedClient(s.NewLecturer); using var second = app.CreateAuthenticatedClient(s.Lecturer);
        await AssertOneWinner(await Task.WhenAll(Respond(first, a.Id, "accept"), Respond(second, b.Id, "accept")));
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(x => x.ProjectId == m.Project.Id && x.MajorId == m.FirstMajor && x.EndedAt == null));
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(x => x.ProjectId == m.Project.Id && x.IsPrimary && x.EndedAt == null));
    }

    [Fact]
    public async Task Replacement_of_discipline_mentor_preserves_major_and_primary_and_rechecks_capacity()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(m.Project.LeaderId);
        using var mentor = app.CreateAuthenticatedClient(s.NewLecturer); using var staff = app.CreateAuthenticatedClient(s.Staff);
        var request = await Body<SupervisorRequestDto>(await SendMentor(leader, m.Project.Id, m.Mentor, m.FirstMajor));
        var accepted = await Body<SupervisorRequestDto>(await Respond(mentor, request.Id, "accept"));
        var url = $"/api/v1/supervisor-assignments/{accepted.AssignmentId}/replace";
        var input = new { supervisorProfileId = s.ProfileId, reason = "Mentor availability" };
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, input)).StatusCode);
        await using (var db = database.CreateContext())
        {
            db.SupervisorExpertises.Add(new() { SupervisorProfileId = s.ProfileId, ExpertiseName = "SE" });
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 0;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, input)).StatusCode);
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 1;
            await db.SaveChangesAsync();
        }
        var replacement = await Body<SupervisorAssignmentDto>(await staff.PostAsJsonAsync(url, input));
        Assert.False(replacement.IsPrimary); Assert.Equal("DISCIPLINE_MENTOR", replacement.AssignmentType); Assert.Equal(m.FirstMajor, replacement.MajorId);
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync($"/api/v1/milestones/project/{m.Project.Id}")).StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(x => x.ProjectId == m.Project.Id && x.IsPrimary && x.EndedAt == null));
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(x => x.ProjectId == m.Project.Id && x.MajorId == m.FirstMajor && x.EndedAt == null));
    }

    [Fact]
    public async Task Concurrent_replacements_cannot_fork_assignment_history()
    {
        var s = await database.SeedAsync(); var m = await MentorProject(s);
        long assignmentId;
        await using (var db = database.CreateContext())
        {
            (await db.Users.FindAsync(s.OtherLecturer))!.DepartmentId = s.DepartmentId;
            assignmentId = await db.SupervisorAssignments.Where(a => a.ProjectId == m.Project.Id && a.IsPrimary).Select(a => a.Id).SingleAsync();
            await db.SaveChangesAsync();
        }
        var alternative = await AddProfile(s.OtherLecturer);
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_profiles"), clock: new Clock());
        using var first = app.CreateAuthenticatedClient(s.Staff); using var second = app.CreateAuthenticatedClient(s.Staff);
        var url = $"/api/v1/supervisor-assignments/{assignmentId}/replace";
        await AssertOneWinner(await Task.WhenAll(
            first.PostAsJsonAsync(url, new { supervisorProfileId = m.Mentor, reason = "First replacement" }),
            second.PostAsJsonAsync(url, new { supervisorProfileId = alternative, reason = "Second replacement" })));
        await using var check = database.CreateContext();
        Assert.NotNull((await check.SupervisorAssignments.FindAsync(assignmentId))!.EndedAt);
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(a => a.ReplacesAssignmentId == assignmentId));
        Assert.Equal(1, await check.SupervisorAssignments.CountAsync(a => a.ProjectId == m.Project.Id && a.IsPrimary && a.EndedAt == null));
        Assert.Equal("ACTIVE", (await check.Projects.FindAsync(m.Project.Id))!.Status);
    }
}
