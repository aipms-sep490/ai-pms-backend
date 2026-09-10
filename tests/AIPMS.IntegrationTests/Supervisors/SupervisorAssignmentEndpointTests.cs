using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Milestones.DTOs;
using AIPMS.Application.Features.Supervisors.DTOs;
using AIPMS.Application.Features.Tasks.DTOs;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.IntegrationTests.Supervisors;

public sealed class SupervisorAssignmentEndpointTests(SupervisorDatabaseFixture database) : IClassFixture<SupervisorDatabaseFixture>
{
    private static readonly DateTime Now = new(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
    private static string AssignmentUrl(long id) => $"/api/v1/supervisor-assignments/{id}";
    private static string ProjectUrl(long id) => $"/api/v1/projects/{id}/supervisor-assignments";
    private static Task<HttpResponseMessage> End(HttpClient client, long id, string reason = " Guidance completed ") =>
        client.PostAsJsonAsync(AssignmentUrl(id) + "/end", new EndSupervisorAssignmentRequest(reason));
    private static async Task<T> Body<T>(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Acceptance_enables_existing_workspace_and_replay_preserves_milestones_and_tasks()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        using var student = app.CreateAuthenticatedClient(s.Student);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var milestoneBody = new { projectId = p.Id, title = "Delivery", sortOrder = 1 };
        Assert.Equal(HttpStatusCode.Conflict, (await student.PostAsJsonAsync("/api/v1/milestones", milestoneBody)).StatusCode);
        var accepted = await Accept(app, s, p.Id);
        var assignment = await Body<SupervisorAssignmentDto>(await lecturer.GetAsync(AssignmentUrl(accepted.AssignmentId!.Value)));
        Assert.Equal(s.ProfileId, assignment.SupervisorProfileId);
        Assert.Equal(accepted.Id, assignment.SupervisorRequestId);
        Assert.Equal(assignment, Assert.Single((await Body<PagedResult<SupervisorAssignmentDto>>(await student.GetAsync(ProjectUrl(p.Id)))).Items));
        var milestone = await Body<MilestoneDto>(await lecturer.PostAsJsonAsync("/api/v1/milestones", milestoneBody));
        var task = await Body<TaskDto>(await student.PostAsJsonAsync("/api/v1/tasks", new
        {
            milestoneId = milestone.Id, title = "Implement", priority = "MEDIUM", assigneeUserIds = new[] { s.Student }
        }));
        await Body<SupervisorRequestDto>(await lecturer.PostAsJsonAsync($"/api/v1/supervisor-requests/{accepted.Id}/accept", new RespondToSupervisorRequest(null)));
        await using var db = database.CreateContext();
        Assert.Equal(p.TeamId, (await db.Projects.FindAsync(p.Id))!.TeamId);
        Assert.Equal(milestone.Id, (await db.Milestones.SingleAsync(m => m.ProjectId == p.Id)).Id);
        Assert.Equal(task.Id, (await db.Tasks.SingleAsync(t => t.MilestoneId == milestone.Id)).Id);
        Assert.Equal(2, await db.ProjectStatusHistories.CountAsync(h => h.ProjectId == p.Id));
    }

    [Theory]
    [InlineData("COMPLETED", "lecturer")]
    [InlineData("ARCHIVED", "admin")]
    [InlineData("COMPLETED", "staff")]
    public async Task End_records_reason_once_preserves_history_and_releases_both_capacity_limits(string status, string actor)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var accepted = await Accept(app, s, p.Id);
        await using (var db = database.CreateContext())
        {
            (await db.Projects.FindAsync(p.Id))!.Status = status;
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 1;
            await db.SaveChangesAsync();
        }
        var next = await SeedProject(s, p.SemesterId);
        using var admin = app.CreateAuthenticatedClient(s.Admin);
        var candidateUrl = $"/api/v1/projects/{next.Id}/supervisor-candidates";
        Assert.Empty((await Body<PagedResult<SupervisorCandidateDto>>(await admin.GetAsync(candidateUrl))).Items);
        var actorId = actor == "lecturer" ? s.Lecturer : actor == "staff" ? s.Staff : s.Admin;
        using var client = app.CreateAuthenticatedClient(actorId);
        var ended = await Body<SupervisorAssignmentDto>(await End(client, accepted.AssignmentId!.Value));
        Assert.Equal(Now, ended.EndedAt);
        Assert.Equal(ended, await Body<SupervisorAssignmentDto>(await End(client, ended.Id, "Replay reason")));
        var candidate = Assert.Single((await Body<PagedResult<SupervisorCandidateDto>>(await admin.GetAsync(candidateUrl))).Items);
        Assert.Equal(0, candidate.ActiveProjects);
        Assert.Equal(0, candidate.SemesterActiveProjects);
        Assert.Equal(1, candidate.RemainingSlots);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(ended, await Body<SupervisorAssignmentDto>(await lecturer.GetAsync(AssignmentUrl(ended.Id))));
        Assert.Equal(ended, Assert.Single((await Body<PagedResult<SupervisorAssignmentDto>>(await lecturer.GetAsync("/api/v1/supervisors/assignments?status=ENDED"))).Items));
        Assert.Empty((await Body<PagedResult<SupervisorAssignmentDto>>(await lecturer.GetAsync("/api/v1/supervisors/assignments?status=ACTIVE"))).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync($"/api/v1/milestones/project/{p.Id}")).StatusCode);
        await Body<SupervisorRequestDto>(await lecturer.PostAsJsonAsync($"/api/v1/supervisor-requests/{accepted.Id}/accept", new RespondToSupervisorRequest(null)));
        await using var check = database.CreateContext();
        Assert.Equal(status, (await check.Projects.FindAsync(p.Id))!.Status);
        Assert.Equal("ACCEPTED", (await check.SupervisorRequests.FindAsync(accepted.Id))!.Status);
        var audit = await check.AuditLogs.SingleAsync(a => a.Action == "SUPERVISOR_ASSIGNMENT_ENDED" && a.EntityId == ended.Id.ToString());
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal("SUPERVISOR_ASSIGNMENT", audit.EntityType);
        Assert.Contains("Guidance completed", audit.DetailsJson!);
        Assert.DoesNotContain("Replay reason", audit.DetailsJson!);
        Assert.Equal(2, await check.ProjectStatusHistories.CountAsync(h => h.ProjectId == p.Id));
    }

    [Fact]
    public async Task Project_read_access_does_not_grant_end_permission_and_token_claims_do_not_override_roles()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var a = await Accept(app, s, p.Id);
        await SetStatus(p.Id, "COMPLETED");
        using var student = app.CreateAuthenticatedClient(s.Student, roles: AppRoles.Admin);
        await Body<PagedResult<SupervisorAssignmentDto>>(await student.GetAsync(ProjectUrl(p.Id)));
        Assert.Equal(HttpStatusCode.Forbidden, (await End(student, a.AssignmentId!.Value)).StatusCode);
        foreach (var id in new[] { s.OtherLecturer, s.OutsideStaff, s.NewLecturer })
        {
            using var outsider = app.CreateAuthenticatedClient(id, roles: AppRoles.Admin);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(AssignmentUrl(a.AssignmentId.Value))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(ProjectUrl(p.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await End(outsider, a.AssignmentId.Value)).StatusCode);
        }
        await using var db = database.CreateContext();
        Assert.Null((await db.SupervisorAssignments.FindAsync(a.AssignmentId.Value))!.EndedAt);
    }

    [Theory]
    [InlineData("ACTIVE")]
    [InlineData("FINAL_SUBMISSION")]
    [InlineData("APPROVED")]
    public async Task End_does_not_leave_unfinished_projects_without_supervisor(string status)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var a = await Accept(app, s, p.Id);
        await SetStatus(p.Id, status);
        using var admin = app.CreateAuthenticatedClient(s.Admin);
        var response = await End(admin, a.AssignmentId!.Value);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions.ContainsKey("traceId"));
        await using var db = database.CreateContext();
        Assert.Null((await db.SupervisorAssignments.FindAsync(a.AssignmentId.Value))!.EndedAt);
        Assert.Equal(status, (await db.Projects.FindAsync(p.Id))!.Status);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("department")]
    [InlineData("account")]
    public async Task End_rechecks_persisted_actor_even_on_replay(string change)
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var a = await Accept(app, s, p.Id);
        await SetStatus(p.Id, "COMPLETED");
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer, roles: AppRoles.Lecturer);
        await Body<SupervisorAssignmentDto>(await End(lecturer, a.AssignmentId!.Value));
        await using (var db = database.CreateContext())
        {
            if (change == "role") db.UserRoles.RemoveRange(await db.UserRoles.Where(r => r.UserId == s.Lecturer).ToListAsync());
            if (change == "department") (await db.Departments.FindAsync(s.DepartmentId))!.IsActive = false;
            if (change == "account") (await db.Users.FindAsync(s.Lecturer))!.Status = "INACTIVE";
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await End(lecturer, a.AssignmentId.Value)).StatusCode);
    }

    [Fact]
    public async Task End_rolls_back_timestamp_when_audit_fails()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var a = await Accept(app, s, p.Id);
        await SetStatus(p.Id, "COMPLETED");
        await using var db = database.CreateContext();
        var original = await db.SupervisorAssignments.AsNoTracking().SingleAsync(x => x.Id == a.AssignmentId);
        using var failing = new SupervisorFactory(database, failAudit: true, clock: new Clock());
        using var lecturer = failing.CreateAuthenticatedClient(s.Lecturer);
        Assert.Equal(HttpStatusCode.InternalServerError, (await End(lecturer, original.Id)).StatusCode);
        var persisted = await db.SupervisorAssignments.AsNoTracking().SingleAsync(x => x.Id == original.Id);
        Assert.Null(persisted.EndedAt);
        Assert.Equal(original.UpdatedAt, persisted.UpdatedAt);
        Assert.False(await db.AuditLogs.AnyAsync(x => x.Action == "SUPERVISOR_ASSIGNMENT_ENDED" && x.EntityId == original.Id.ToString()));
    }

    [Fact]
    public async Task Concurrent_end_is_idempotent_and_audits_once()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        var a = await Accept(setup, s, p.Id);
        await SetStatus(p.Id, "COMPLETED");
        using var app = new SupervisorFactory(database, saveInterceptor: new EndBarrier(), clock: new Clock());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var responses = await Task.WhenAll(End(lecturer, a.AssignmentId!.Value), End(lecturer, a.AssignmentId.Value));
        var first = await Body<SupervisorAssignmentDto>(responses[0]);
        Assert.Equal(first, await Body<SupervisorAssignmentDto>(responses[1]));
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "SUPERVISOR_ASSIGNMENT_ENDED" && x.EntityId == first.Id.ToString()));
    }

    [Fact]
    public async Task Ending_while_accepting_other_work_never_overbooks_and_can_retry_after_release()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var next = await SeedProject(s);
        using var setup = new SupervisorFactory(database, clock: new Clock());
        using var student = setup.CreateAuthenticatedClient(s.Student);
        var pending = await Body<SupervisorRequestDto>(await student.PostAsJsonAsync($"/api/v1/projects/{next.Id}/supervisor-requests",
            new SendSupervisorRequest(s.ProfileId, null)));
        var accepted = await Accept(setup, s, p.Id);
        await SetStatus(p.Id, "COMPLETED");
        await using (var db = database.CreateContext())
        {
            (await db.SupervisorProfiles.FindAsync(s.ProfileId))!.MaxActiveProjects = 1;
            await db.SaveChangesAsync();
        }
        using var app = new SupervisorFactory(database, saveInterceptor: new EndBarrier("supervisor_profiles"), clock: new Clock());
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var acceptUrl = $"/api/v1/supervisor-requests/{pending.Id}/accept";
        var results = await Task.WhenAll(End(lecturer, accepted.AssignmentId!.Value),
            lecturer.PostAsJsonAsync(acceptUrl, new RespondToSupervisorRequest(null)));
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        foreach (var conflict in results.Where(r => r.StatusCode == HttpStatusCode.Conflict))
            Assert.True((await conflict.Content.ReadFromJsonAsync<ProblemDetails>())!.Extensions.ContainsKey("traceId"));
        await using (var db = database.CreateContext())
            Assert.InRange(await db.SupervisorAssignments.CountAsync(a => a.SupervisorProfileId == s.ProfileId && a.EndedAt == null), 0, 1);
        await Body<SupervisorAssignmentDto>(await End(lecturer, accepted.AssignmentId.Value));
        var newAssignment = await Body<SupervisorRequestDto>(await lecturer.PostAsJsonAsync(acceptUrl, new RespondToSupervisorRequest(null)));
        await using var check = database.CreateContext();
        Assert.Equal(newAssignment.AssignmentId, (await check.SupervisorAssignments.SingleAsync(a => a.SupervisorProfileId == s.ProfileId && a.EndedAt == null)).Id);
        Assert.Equal(1, await check.AuditLogs.CountAsync(a => a.Action == "SUPERVISOR_ASSIGNMENT_ENDED" && a.EntityId == accepted.AssignmentId.Value.ToString()));
    }

    [Fact]
    public async Task Lists_are_scoped_filtered_and_paginated_and_invalid_input_is_rejected()
    {
        var s = await database.SeedAsync();
        var p = await SeedProject(s);
        var p2 = await SeedProject(s);
        using var app = new SupervisorFactory(database, clock: new Clock());
        var first = await Accept(app, s, p.Id);
        var second = await Accept(app, s, p2.Id);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        using var other = app.CreateAuthenticatedClient(s.NewLecturer);
        using var admin = app.CreateAuthenticatedClient(s.Admin);
        using var anonymous = app.CreateClient();
        var page = await Body<PagedResult<SupervisorAssignmentDto>>(await lecturer.GetAsync("/api/v1/supervisors/assignments?page=2&pageSize=1"));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.AssignmentId, Assert.Single(page.Items).Id);
        Assert.NotEqual(first.AssignmentId, second.AssignmentId);
        Assert.Empty((await Body<PagedResult<SupervisorAssignmentDto>>(await other.GetAsync("/api/v1/supervisors/assignments"))).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/supervisors/assignments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ProjectUrl(p.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await End(anonymous, first.AssignmentId!.Value)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await End(lecturer, first.AssignmentId.Value, " ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await End(lecturer, first.AssignmentId.Value, new string('x', 2001))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await End(lecturer, 0)).StatusCode);
        foreach (var query in new[] { "status=INVALID", "page=0", "pageSize=101" })
            Assert.Equal(HttpStatusCode.BadRequest, (await lecturer.GetAsync("/api/v1/supervisors/assignments?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await End(admin, long.MaxValue)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(AssignmentUrl(long.MaxValue))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(ProjectUrl(long.MaxValue))).StatusCode);
    }

    private async Task SetStatus(long projectId, string status)
    {
        await using var db = database.CreateContext();
        (await db.Projects.FindAsync(projectId))!.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<SupervisorRequestDto> Accept(SupervisorFactory app, SupervisorScenario s, long projectId)
    {
        using var student = app.CreateAuthenticatedClient(s.Student);
        using var lecturer = app.CreateAuthenticatedClient(s.Lecturer);
        var r = await Body<SupervisorRequestDto>(await student.PostAsJsonAsync($"/api/v1/projects/{projectId}/supervisor-requests", new SendSupervisorRequest(s.ProfileId, null)));
        return await Body<SupervisorRequestDto>(await lecturer.PostAsJsonAsync($"/api/v1/supervisor-requests/{r.Id}/accept", new RespondToSupervisorRequest(null)));
    }

    private async Task<(long Id, long TeamId, long SemesterId)> SeedProject(SupervisorScenario s, long? semesterId = null)
    {
        await using var db = database.CreateContext();
        var semester = semesterId.HasValue ? (await db.AcademicSemesters.FindAsync(semesterId))! : new AcademicSemester
        {
            OrganizationId = (await db.Departments.FindAsync(s.DepartmentId))!.OrganizationId,
            Code = Guid.NewGuid().ToString("N"), Name = "Semester", Status = "ACTIVE",
            StartDate = DateOnly.FromDateTime(Now.AddDays(-30)), EndDate = DateOnly.FromDateTime(Now.AddDays(30))
        };
        if (!semesterId.HasValue)
        {
            db.ProjectPeriods.Add(new() { AcademicSemester = semester, Code = "SELECT", Name = "Selection", Status = "ACTIVE",
                PeriodType = "SUPERVISOR_SELECTION", StartAt = Now.AddDays(-1), EndAt = Now.AddDays(1), MaxProjectsPerSupervisor = 1 });
            await db.SaveChangesAsync();
        }
        var project = new Project { Code = Guid.NewGuid().ToString("N"), Title = "Project", CreatedBy = s.Student,
            Status = "APPROVED", RegisteredAt = Now, Team = new() { AcademicSemesterId = semester.Id,
                Code = Guid.NewGuid().ToString("N"), Name = "Team", Status = "ELIGIBLE", CreatedBy = s.Student,
                TeamMembers = semesterId.HasValue ? [] : [new() { UserId = s.Student, AcademicSemesterId = semester.Id, IsLeader = true }] },
            ProjectMajors = [new() { Major = new() { Code = Guid.NewGuid().ToString("N"), Name = "SE",
                DepartmentId = s.DepartmentId, IsActive = true } }] };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (project.Id, project.TeamId, semester.Id);
    }

    private sealed class EndBarrier(string table = "supervisor_assignments") : DbCommandInterceptor
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains($"dbo.{table} WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal)
                && Volatile.Read(ref arrivals) < 2)
            {
                if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
