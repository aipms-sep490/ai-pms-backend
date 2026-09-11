using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed partial class SupervisorRequestEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private static string ProjectUrl(long id) => $"/api/v1/projects/{id}/supervisor-requests";
    private static string ActionUrl(long id, string action) => $"/api/v1/supervisor-requests/{id}/{action}";
    private static Task<HttpResponseMessage> Send(HttpClient client, long project, long profile, string? message = " Please advise ") =>
        client.PostAsJsonAsync(ProjectUrl(project), new SendSupervisorRequest(profile, message));
    private static Task<HttpResponseMessage> Respond(HttpClient client, long id, string action) =>
        client.PostAsJsonAsync(ActionUrl(id, action), new RespondToSupervisorRequest(" Decision "));
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Send_cancel_reject_resend_and_scoped_inboxes_preserve_history()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        using var other = app.CreateAuthenticatedClient(s.NewLecturer);
        var first = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        Assert.Equal("PENDING", first.Status);
        Assert.Equal("Please advise", first.RequestMessage);
        Assert.Equal(p.LeaderId, first.RequestedBy);
        Assert.Null(first.AssignmentId);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(leader, p.Id, s.ProfileId)).StatusCode);
        var inbox = await Body<PagedResult<SupervisorRequestDto>>(await lecturer.GetAsync("/api/v1/supervisors/requests?status=PENDING"));
        Assert.Equal(first.Id, Assert.Single(inbox.Items).Id);
        Assert.Empty((await Body<PagedResult<SupervisorRequestDto>>(await other.GetAsync("/api/v1/supervisors/requests"))).Items);
        var cancel = await Body<SupervisorRequestDto>(await leader.PostAsync(ActionUrl(first.Id, "cancel"), null));
        Assert.Equal("CANCELLED", cancel.Status);
        Assert.Equal(Now, cancel.RespondedAt);
        await Body<SupervisorRequestDto>(await leader.PostAsync(ActionUrl(first.Id, "cancel"), null));
        var second = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        Assert.NotEqual(first.Id, second.Id);
        var reject = await Body<SupervisorRequestDto>(await Respond(lecturer, second.Id, "reject"));
        Assert.Equal("REJECTED", reject.Status);
        Assert.Equal("Decision", reject.ResponseMessage);
        await Body<SupervisorRequestDto>(await Respond(lecturer, second.Id, "reject"));
        Assert.Equal(HttpStatusCode.Conflict, (await Respond(lecturer, second.Id, "accept")).StatusCode);
        var third = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var page = await Body<PagedResult<SupervisorRequestDto>>(await leader.GetAsync(ProjectUrl(p.Id) + "?page=2&pageSize=1"));
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(second.Id, Assert.Single(page.Items).Id);
        Assert.NotEqual(second.Id, third.Id);
        await using var db = database.CreateContext();
        Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
        Assert.Equal("APPROVED", (await db.Projects.FindAsync(p.Id))!.Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_REQUEST_CANCELLED" && a.EntityId == first.Id.ToString()));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_REQUEST_REJECTED" && a.EntityId == second.Id.ToString()));
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("ARCHIVED")]
    public async Task Accept_creates_assignment_activates_atomically_and_replay_never_reopens_project(string finalStatus)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var secondProfile = await AddProfile(s.NewLecturer);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var other = await Body<SupervisorRequestDto>(await Send(leader, p.Id, secondProfile));
        var accepted = await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "accept"));
        Assert.Equal("ACCEPTED", accepted.Status);
        Assert.NotNull(accepted.AssignmentId);
        await using var db = database.CreateContext();
        var assignment = await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == p.Id);
        Assert.Equal(s.ProfileId, assignment.SupervisorProfileId);
        Assert.Equal(request.Id, assignment.SupervisorRequestId);
        Assert.Equal(accepted.AssignmentId, assignment.Id);
        Assert.True(assignment.IsPrimary);
        Assert.Null(assignment.EndedAt);
        Assert.Equal("ACTIVE", (await db.Projects.FindAsync(p.Id))!.Status);
        Assert.Equal("CANCELLED", (await db.SupervisorRequests.FindAsync(other.Id))!.Status);
        var history = await db.ProjectStatusHistories.Where(h => h.ProjectId == p.Id).OrderBy(h => h.Id).ToListAsync();
        Assert.Equal(new[] { "SUPERVISOR_PENDING", "ACTIVE" }, history.Select(h => h.NewStatus));
        Assert.All(history, h => Assert.Equal(s.Lecturer, h.ChangedBy));
        Assert.Equal("PROJECT", (await db.AuditLogs.SingleAsync(a => a.Action == "PROJECT_ACTIVATED"
            && a.EntityId == p.Id.ToString())).EntityType);
        Assert.Equal("SUPERVISOR_ASSIGNMENT", (await db.AuditLogs.SingleAsync(a => a.Action == "SUPERVISOR_ASSIGNED"
            && a.EntityId == assignment.Id.ToString())).EntityType);
        var audits = await db.AuditLogs.CountAsync();
        var repeat = await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "accept"));
        Assert.Equal(accepted, repeat);
        (await db.Projects.FindAsync(p.Id))!.Status = finalStatus;
        assignment.EndedAt = Now;
        await db.SaveChangesAsync();
        await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "accept"));
        Assert.Equal(finalStatus, (await db.Projects.AsNoTracking().SingleAsync(x => x.Id == p.Id)).Status);
        Assert.Equal(Now, (await db.SupervisorAssignments.AsNoTracking().SingleAsync(a => a.ProjectId == p.Id)).EndedAt);
        Assert.Equal(audits, await db.AuditLogs.CountAsync());
        Assert.Equal(2, await db.ProjectStatusHistories.CountAsync(h => h.ProjectId == p.Id));
    }

    [Fact]
    public async Task Permissions_use_current_leader_and_persisted_recipient_not_view_access_or_role_claims()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, p.Id, s.ProfileId)).StatusCode);
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        foreach (var user in new[] { s.Admin, s.Staff, p.MemberId, s.OtherLecturer })
        {
            using var unauthorized = app.CreateAuthenticatedClient(user, roles: AppRoles.Admin);
            Assert.Equal(HttpStatusCode.Forbidden, (await Send(unauthorized, p.Id, s.ProfileId)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await unauthorized.PostAsync(ActionUrl(request.Id, "cancel"), null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Respond(unauthorized, request.Id, "accept")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Respond(unauthorized, request.Id, "reject")).StatusCode);
        }
        using var outsider = app.CreateAuthenticatedClient(s.Student);
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(ProjectUrl(p.Id))).StatusCode);
        using var member = app.CreateAuthenticatedClient(p.MemberId);
        Assert.Single((await Body<PagedResult<SupervisorRequestDto>>(await member.GetAsync(ProjectUrl(p.Id)))).Items);
        await using (var db = database.CreateContext())
        {
            // Avoid transient unique-index collisions when transferring leadership.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.team_members SET is_leader = 0 WHERE team_id = {p.TeamId}");
            db.ChangeTracker.Clear();
            (await db.TeamMembers.SingleAsync(m => m.TeamId == p.TeamId && m.UserId == p.MemberId)).IsLeader = true;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await leader.PostAsync(ActionUrl(request.Id, "cancel"), null)).StatusCode);
        Assert.Equal("CANCELLED", (await Body<SupervisorRequestDto>(await member.PostAsync(ActionUrl(request.Id, "cancel"), null))).Status);
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("availability")]
    [InlineData("period")]
    [InlineData("project")]
    [InlineData("department")]
    public async Task Accept_rechecks_eligibility_after_request_was_sent(string change)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        await using (var db = database.CreateContext())
        {
            if (change == "capacity") (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 0;
            if (change == "availability") (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.IsAvailable = false;
            if (change == "period") (await db.ProjectPeriods.FindAsync(p.PeriodId))!.Status = "CLOSED";
            if (change == "project") (await db.Projects.FindAsync(p.Id))!.Status = "ARCHIVED";
            if (change == "department") (await db.Users.FindAsync(s.Lecturer))!.DepartmentId =
                (await db.Users.FindAsync(s.OtherLecturer))!.DepartmentId;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await Respond(lecturer, request.Id, "accept")).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal("PENDING", (await verify.SupervisorRequests.FindAsync(request.Id))!.Status);
        Assert.False(await verify.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
        Assert.False(await verify.ProjectStatusHistories.AnyAsync(h => h.ProjectId == p.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audit_failure_rolls_back_send_and_all_accept_side_effects(bool failLastAudit)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var failing = new SupervisorFactory(database, failAudit: !failLastAudit,
            saveInterceptor: failLastAudit ? new FailActivationAudit() : null, clock: new Clock());
        using var failingLeader = failing.CreateAuthenticatedClient(p.LeaderId);
        if (!failLastAudit)
            Assert.Equal(HttpStatusCode.InternalServerError, (await Send(failingLeader, p.Id, s.ProfileId)).StatusCode);
        await using var db = database.CreateContext();
        Assert.False(await db.SupervisorRequests.AnyAsync(r => r.ProjectId == p.Id));
        using var healthy = new SupervisorFactory(database, clock: new Clock());
        using var leader = healthy.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var other = await Body<SupervisorRequestDto>(await Send(leader, p.Id, await AddProfile(s.NewLecturer)));
        var auditCount = await db.AuditLogs.CountAsync();
        using var lecturer = failing.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(HttpStatusCode.InternalServerError, (await Respond(lecturer, request.Id, "accept")).StatusCode);
        Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(request.Id))!.Status);
        Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(other.Id))!.Status);
        Assert.Equal("APPROVED", (await db.Projects.FindAsync(p.Id))!.Status);
        Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
        Assert.False(await db.ProjectStatusHistories.AnyAsync(h => h.ProjectId == p.Id));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == "SUPERVISOR_REQUEST_ACCEPTED" && a.EntityId == request.Id.ToString()));
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Concurrent_replay_of_same_accept_creates_one_assignment_and_one_audit()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var leader = setup.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_requests"), clock: new Clock());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var responses = await System.Threading.Tasks.Task.WhenAll(Respond(lecturer, request.Id, "accept"), Respond(lecturer, request.Id, "accept"));
        var first = await Body<SupervisorRequestDto>(responses[0]);
        var second = await Body<SupervisorRequestDto>(responses[1]);
        Assert.Equal(first, second);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.SupervisorAssignments.CountAsync(a => a.ProjectId == p.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_REQUEST_ACCEPTED" && a.EntityId == request.Id.ToString()));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.NotificationType == "SUPERVISOR_REQUEST_ACCEPTED" && n.RelatedEntityId == request.Id));
    }

    [Fact]
    public async Task Accept_rechecks_current_time_after_waiting_for_capacity_lock()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var leader = setup.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var clock = new AdvancingClock();
        using var app = new SupervisorFactory(database, saveInterceptor: new ExpirePeriodDuringLock(clock), clock: clock);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(HttpStatusCode.Conflict, (await Respond(lecturer, request.Id, "accept")).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal("PENDING", (await db.SupervisorRequests.FindAsync(request.Id))!.Status);
        Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_request_without_matching_assignment_is_not_silently_repaired(bool mismatched)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        await using var db = database.CreateContext();
        (await db.SupervisorRequests.FindAsync(request.Id))!.Status = "ACCEPTED";
        if (mismatched)
        {
            var other = await SeedProject(s);
            db.SupervisorAssignments.Add(new() { ProjectId = other.Id, SupervisorProfileId = s.ProfileId,
                SupervisorRequestId = request.Id, AssignedAt = Now, IsPrimary = true });
        }
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Respond(lecturer, request.Id, "accept")).StatusCode);
        Assert.Equal("APPROVED", (await db.Projects.AsNoTracking().SingleAsync(x => x.Id == p.Id)).Status);
        Assert.False(await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == p.Id));
    }

    [Fact]
    public async Task Concurrent_duplicate_sends_create_one_request_and_return_409_for_losers()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(4, "supervisor_profiles"), clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Send(leader, p.Id, s.ProfileId)));
        await AssertOneWinner(results);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.SupervisorRequests.CountAsync(r => r.ProjectId == p.Id));
        var request = await db.SupervisorRequests.SingleAsync(r => r.ProjectId == p.Id);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_REQUEST_SENT" && a.EntityId == request.Id.ToString()));
        Assert.Equal(1, await db.Notifications.CountAsync(n => n.NotificationType == "SUPERVISOR_REQUEST_SENT" && n.RelatedEntityId == request.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_accepts_cannot_exceed_last_global_or_semester_slot(bool sameSemester)
    {
        var s = await database.SeedAsync();
        var first = await SeedProject(s, quota: sameSemester ? 1 : 5);
        var second = await SeedProject(s, sameSemester ? first.SemesterId : null);
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = sameSemester ? 10 : 1;
            await db.SaveChangesAsync();
        }
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var a = setup.CreateAuthenticatedClient(first.LeaderId);
        using var b = setup.CreateAuthenticatedClient(second.LeaderId);
        var r1 = await Body<SupervisorRequestDto>(await Send(a, first.Id, s.ProfileId));
        var r2 = await Body<SupervisorRequestDto>(await Send(b, second.Id, s.ProfileId));
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_profiles"), clock: new Clock());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var results = await System.Threading.Tasks.Task.WhenAll(Respond(lecturer, r1.Id, "accept"), Respond(lecturer, r2.Id, "accept"));
        await AssertOneWinner(results);
        await using var verify = database.CreateContext();
        Assert.Equal(1, await verify.SupervisorAssignments.CountAsync(x => x.SupervisorProfileId == s.ProfileId && x.EndedAt == null));
        Assert.Equal(1, await verify.SupervisorRequests.CountAsync(r => (r.Id == r1.Id || r.Id == r2.Id) && r.Status == "ACCEPTED"));
        Assert.Equal(1, await verify.Projects.CountAsync(p => (p.Id == first.Id || p.Id == second.Id) && p.Status == "ACTIVE"));
    }

    [Fact]
    public async Task Concurrent_supervisors_cannot_both_win_the_same_project()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var leader = setup.CreateAuthenticatedClient(p.LeaderId);
        var r1 = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        var r2 = await Body<SupervisorRequestDto>(await Send(leader, p.Id, await AddProfile(s.NewLecturer)));
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_profiles"), clock: new Clock());
        using var first = app.CreateAuthenticatedClient(s.Lecturer);
        using var second = app.CreateAuthenticatedClient(s.NewLecturer);
        await AssertOneWinner(await System.Threading.Tasks.Task.WhenAll(Respond(first, r1.Id, "accept"), Respond(second, r2.Id, "accept")));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.SupervisorAssignments.CountAsync(a => a.ProjectId == p.Id));
        Assert.Equal(1, await db.SupervisorRequests.CountAsync(r => r.ProjectId == p.Id && r.Status == "ACCEPTED"));
        Assert.Equal(1, await db.SupervisorRequests.CountAsync(r => r.ProjectId == p.Id && r.Status == "CANCELLED"));
    }

    [Fact]
    public async Task Cancel_racing_accept_never_overwrites_another_decision()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var sender = setup.CreateAuthenticatedClient(p.LeaderId);
        var request = await Body<SupervisorRequestDto>(await Send(sender, p.Id, s.ProfileId));
        using var app = new SupervisorFactory(database, saveInterceptor: new LockBarrier(2, "supervisor_requests"), clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        await AssertOneWinner(await System.Threading.Tasks.Task.WhenAll(
            leader.PostAsync(ActionUrl(request.Id, "cancel"), null), Respond(lecturer, request.Id, "accept")));
        await using var db = database.CreateContext();
        var final = (await db.SupervisorRequests.FindAsync(request.Id))!.Status;
        Assert.Contains(final, new[] { "ACCEPTED", "CANCELLED" });
        Assert.Equal(final == "ACCEPTED" ? 1 : 0, await db.SupervisorAssignments.CountAsync(a => a.ProjectId == p.Id));
        Assert.Equal(final == "ACCEPTED" ? "ACTIVE" : "APPROVED", (await db.Projects.FindAsync(p.Id))!.Status);
    }

    [Fact]
    public async Task Validation_and_cleanup_after_closed_period_return_problem_details()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var leader = app.CreateAuthenticatedClient(p.LeaderId);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(leader, p.Id, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(leader, p.Id, s.ProfileId, new string('x', 2001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.GetAsync(ProjectUrl(p.Id) + "?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await lecturer.GetAsync("/api/v1/supervisors/requests?status=OTHER")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Respond(lecturer, long.MaxValue, "accept")).StatusCode);
        var request = await Body<SupervisorRequestDto>(await Send(leader, p.Id, s.ProfileId));
        await using (var db = database.CreateContext())
        {
            (await db.ProjectPeriods.FindAsync(p.PeriodId))!.Status = "CLOSED";
            await db.SaveChangesAsync();
        }
        Assert.Equal("REJECTED", (await Body<SupervisorRequestDto>(await Respond(lecturer, request.Id, "reject"))).Status);
    }

    private static async Task AssertOneWinner(HttpResponseMessage[] results)
    {
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        foreach (var response in results.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Equal(409, problem!.Status);
            Assert.True(problem.Extensions.ContainsKey("traceId"));
        }
    }

    private async Task<long> AddProfile(long userId)
    {
        await using var db = database.CreateContext();
        var profile = new SupervisorProfile { UserId = userId, IsAvailable = true, MaxActiveProjects = 5 };
        db.SupervisorProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    private async Task<RequestProject> SeedProject(SupervisorScenario s, long? semesterId = null, int quota = 5)
    {
        await using var db = database.CreateContext();
        var roleId = await db.Roles.Where(r => r.Code == AppRoles.Student).Select(r => r.Id).SingleAsync();
        User Student() => new() { Email = $"{Guid.NewGuid():N}@test.local", FullName = "Student", Status = "ACTIVE",
            PasswordHash = "unused", DepartmentId = s.DepartmentId, UserRoleUsers = [new() { RoleId = roleId }] };
        var leader = Student();
        var member = Student();
        var semester = semesterId.HasValue ? (await db.AcademicSemesters.FindAsync(semesterId))! : new AcademicSemester
        {
            OrganizationId = (await db.Departments.FindAsync(s.DepartmentId))!.OrganizationId,
            Code = Guid.NewGuid().ToString("N"), Name = "Semester", Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)), EndDate = DateOnly.FromDateTime(Now.AddDays(30))
        };
        ProjectPeriod period;
        if (semesterId.HasValue) period = await db.ProjectPeriods.SingleAsync(p => p.AcademicSemesterId == semesterId);
        else
        {
            period = new() { AcademicSemester = semester, Code = "SELECT", Name = "Selection", Status = "ACTIVE",
                PeriodType = "SUPERVISOR_SELECTION", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(1), MaxProjectsPerSupervisor = quota };
            db.ProjectPeriods.Add(period);
        }
        db.Users.AddRange(leader, member);
        await db.SaveChangesAsync();
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "Project", CreatedBy = leader.Id,
            Status = "APPROVED", RegisteredAt = Now, Team = new() { AcademicSemesterId = semester.Id,
                Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "ELIGIBLE", CreatedBy = leader.Id,
                TeamMembers = [new() { UserId = leader.Id, AcademicSemesterId = semester.Id, IsLeader = true },
                    new() { UserId = member.Id, AcademicSemesterId = semester.Id, IsLeader = false }] },
            ProjectMajors = [new() { Major = new() { Code = Guid.NewGuid().ToString("N"), Name = "SE",
                DepartmentId = s.DepartmentId, IsActive = true } }] };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return new(project.Id, project.TeamId, leader.Id, member.Id, semester.Id, period.Id);
    }

    private sealed record RequestProject(long Id, long TeamId, long LeaderId, long MemberId, long SemesterId, long PeriodId);

    private sealed class FailActivationAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLog>()
                .Any(e => e.State == EntityState.Added && e.Entity.Action == "PROJECT_ACTIVATED"))
                throw new InvalidOperationException("Simulated failure after earlier acceptance writes and audit records.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private long ticks = Now.Ticks;
        public void Expire() => Interlocked.Exchange(ref ticks, Now.AddDays(2).Ticks);
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    }

    private sealed class ExpirePeriodDuringLock(AdvancingClock clock) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("dbo.supervisor_profiles WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal))
                clock.Expire();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LockBarrier(int participants, string table) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"dbo.{table} WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
                && Volatile.Read(ref arrivals) < participants)
            {
                if (Interlocked.Increment(ref arrivals) == participants) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
