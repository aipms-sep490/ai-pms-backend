using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Features.Calendar.DTOs;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class CalendarV5EndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private const string Url = "/api/v1/calendar?from=2026-09-01T00:00:00Z&to=2026-10-01T00:00:00Z";
    private static DateTime Due => new(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
    private static async Task<CalendarResponseDto> Body(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<CalendarResponseDto>())!; }
    private static async Task<T> Read<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private async Task<(EvaluationScenario S, long Primary, long Major)> Seed()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext();
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId).Select(a => a.SupervisorProfile.UserId).SingleAsync();
        var task = new M.Task { Title = "Dated task", CreatedBy = s.Scope.Users.Student, Status = "TODO", DueAt = Due,
            Milestone = new() { ProjectId = s.ProjectId, Title = "Dated milestone", CreatedBy = s.Scope.Users.Student, Status = "PLANNED", DueDate = DateOnly.FromDateTime(Due) } };
        db.Add(task);
        db.Meetings.Add(new() { ProjectId = s.ProjectId, Title = "Meeting", CreatedBy = s.Scope.Users.Student, Status = "SCHEDULED", StartAt = Due, EndAt = Due.AddHours(1) });
        db.Deliverables.Add(new() { ProjectId = s.ProjectId, Title = "Target report", CreatedBy = s.Scope.Users.Student, Status = "OPEN", DueAt = Due,
            EvidenceScope = "MAJOR_SPECIFIC", TargetMajorId = major });
        await db.SaveChangesAsync();
        db.TaskDisciplines.Add(new() { TaskId = task.Id, MajorId = major, Role = "PRIMARY", CreatedBy = s.Scope.Users.Student, CreatedAt = Due });
        await db.SaveChangesAsync(); return (s, primary, major);
    }
    [Fact]
    public async Task Committee_membership_does_not_grant_project_meetings_or_other_calendar_sources()
    {
        using var app = new EvaluationFactory(database); var x = await Seed();
        await using (var db = database.CreateContext())
        {
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Fixture committee", CreatedBy = x.S.Scope.Users.Staff,
                CreatedAt = Due, Members = [new() { UserId = x.S.Scope.Users.NewLecturer, MajorId = x.Major }] };
            db.Add(committee); await db.SaveChangesAsync();
            db.Add(new DefenseSession { ProjectId = x.S.ProjectId, CommitteeId = committee.Id, StartAt = Due, EndAt = Due.AddHours(1),
                Room = "FIXTURE ROOM", CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Due });
            var meeting = await db.Meetings.SingleAsync(m => m.ProjectId == x.S.ProjectId);
            db.MeetingParticipants.Add(new() { MeetingId = meeting.Id, UserId = x.S.Scope.Users.NewLecturer, AttendanceStatus = "INVITED" });
            await db.SaveChangesAsync();
        }
        using var member = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        Assert.Equal("DEFENSE_SESSION", Assert.Single((await Body(await member.GetAsync(Url))).Items).SourceType);
    }
    [Fact]
    public async Task Cursor_keeps_cross_source_ties_and_is_bound_to_actor_range_and_filter()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); var foreign = await Seed();
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var all = await Body(await student.GetAsync(Url)); Assert.Equal(4, all.Items.Count);
        Assert.All(all.Items, i => { Assert.Equal(x.S.ProjectId, i.ProjectId); Assert.Equal(Due, i.DueAt); Assert.Equal(DateTimeKind.Utc, i.DueAt.Kind); });
        var first = await Body(await student.GetAsync(Url + "&pageSize=1")); Assert.True(first.HasMore);
        var items = first.Items.ToList(); var cursor = first.NextCursor;
        while (cursor is not null)
        {
            var page = await Body(await student.GetAsync(Url + "&pageSize=1&cursor=" + Uri.EscapeDataString(cursor)));
            items.AddRange(page.Items); cursor = page.NextCursor; Assert.True(items.Count <= 4);
        }
        Assert.Equal(4, items.Count); Assert.Equal(4, items.Select(i => (i.SourceType, i.SourceId)).Distinct().Count());
        Assert.Equal(HttpStatusCode.BadRequest, (await primary.GetAsync(Url + "&cursor=" + Uri.EscapeDataString(first.NextCursor!))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await student.GetAsync(Url + "&sourceType=TASK&cursor=" + Uri.EscapeDataString(first.NextCursor!))).StatusCode);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin); Assert.Empty((await Body(await admin.GetAsync(Url))).Items);
        using var other = app.CreateAuthenticatedClient(foreign.S.Scope.Users.Student); Assert.DoesNotContain((await Body(await other.GetAsync(Url))).Items, i => i.ProjectId == x.S.ProjectId);
    }
    [Fact]
    public async Task Mentor_gets_only_classified_tasks_targeted_artifacts_and_invited_meetings()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); long assignmentId; long unclassifiedTask;
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = x.S.Scope.Users.NewLecturer };
            var assignment = new M.SupervisorAssignment { ProjectId = x.S.ProjectId, SupervisorProfile = profile,
                SupervisorRequest = new() { ProjectId = x.S.ProjectId, SupervisorProfile = profile, RequestedBy = x.S.Scope.Users.Student,
                    Status = "ACCEPTED", RequestedAt = EvaluationDraftDatabaseFixture.Now.AddDays(-1), RespondedAt = EvaluationDraftDatabaseFixture.Now },
                AssignmentType = "DISCIPLINE_MENTOR", MajorId = x.Major, IsPrimary = false, AssignedAt = EvaluationDraftDatabaseFixture.Now };
            db.Add(assignment);
            var task = new M.Task { MilestoneId = await db.Milestones.Where(m => m.ProjectId == x.S.ProjectId).Select(m => m.Id).SingleAsync(),
                Title = "Unclassified", Status = "TODO", CreatedBy = x.S.Scope.Users.Student, DueAt = Due };
            db.Add(task); await db.SaveChangesAsync(); assignmentId = assignment.Id; unclassifiedTask = task.Id;
        }
        using var mentor = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        var calendar = await Body(await mentor.GetAsync(Url)); Assert.Equal(2, calendar.Items.Count);
        Assert.DoesNotContain(calendar.Items, i => i.SourceType is "MEETING" or "MILESTONE");
        Assert.DoesNotContain(calendar.Items, i => i.SourceType == "TASK" && i.SourceId == unclassifiedTask);
        await using (var db = database.CreateContext())
        {
            var meetingId = await db.Meetings.Where(m => m.ProjectId == x.S.ProjectId).Select(m => m.Id).SingleAsync();
            db.MeetingParticipants.Add(new() { MeetingId = meetingId, UserId = x.S.Scope.Users.NewLecturer, AttendanceStatus = "INVITED" }); await db.SaveChangesAsync();
        }
        Assert.Single((await Body(await mentor.GetAsync(Url + "&sourceType=MEETING"))).Items);
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.FindAsync(assignmentId))!.EndedAt = EvaluationDraftDatabaseFixture.Now; await db.SaveChangesAsync(); }
        Assert.Empty((await Body(await mentor.GetAsync(Url))).Items);
    }
    [Fact]
    public async Task Frozen_department_scope_ignores_changed_live_major_and_unknown_snapshot_is_hidden()
    {
        using var app = new EvaluationFactory(database); var x = await Seed();
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff); using var outsider = app.CreateAuthenticatedClient(x.S.Scope.Users.OutsideStaff);
        Assert.Equal(4, (await Body(await staff.GetAsync(Url))).Items.Count);
        await using (var db = database.CreateContext())
        {
            (await db.Majors.FindAsync(x.Major))!.DepartmentId = (await db.Users.FindAsync(x.S.Scope.Users.OutsideStaff))!.DepartmentId!.Value;
            await db.SaveChangesAsync();
        }
        Assert.Empty((await Body(await outsider.GetAsync(Url))).Items);
        Assert.Equal(4, (await Body(await staff.GetAsync(Url))).Items.Count);
        await using (var db = database.CreateContext()) { (await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == x.S.ProjectId)).SnapshotJson = "{}"; await db.SaveChangesAsync(); }
        Assert.Empty((await Body(await staff.GetAsync(Url))).Items);
    }

    [Fact]
    public async Task Evaluator_sees_own_authoritative_window_without_project_work_and_revocation_removes_it()
    {
        using var app = new EvaluationFactory(database); var s = await database.Seed(true);
        using var staff = app.CreateAuthenticatedClient(s.Scope.Users.Staff); using var evaluator = app.CreateAuthenticatedClient(s.Scope.Users.Lecturer);
        var scheme = await Read<EvaluationSchemeDto>(await staff.PostAsJsonAsync("/api/v1/evaluation-schemes",
            new SaveEvaluationSchemeRequest(s.ProjectId, s.PeriodId, "Calendar demo", 5, [new("Common", "COMMON", null, s.RubricId, 100, 100, 1)])));
        scheme = await Read<EvaluationSchemeDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-schemes/{scheme.Id}/publish", new SchemeTokenRequest(scheme.ConcurrencyToken)));
        var assignment = await Read<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/projects/{s.ProjectId}/evaluation-assignments",
            new AssignEvaluatorRequest(s.Scope.Users.Lecturer, s.PeriodId, "LECTURER", "COMMON", ComponentId: scheme.Components.Single().Id)));
        var calendar = await Body(await evaluator.GetAsync(Url)); var item = Assert.Single(calendar.Items);
        Assert.Equal("EVALUATION_ASSIGNMENT", item.SourceType); Assert.Equal(assignment.Id, item.SourceId);
        Assert.Equal(EvaluationDraftDatabaseFixture.Now.AddDays(1), item.DueAt);
        Assert.DoesNotContain(calendar.Items, i => i.SourceType == "FINAL_SUBMISSION");
        await Read<EvaluationAssignmentDto>(await staff.PostAsJsonAsync($"/api/v1/evaluation-assignments/{assignment.Id}/revoke", new RevokeEvaluatorRequest(assignment.ConcurrencyToken, "Replaced evaluator")));
        Assert.Empty((await Body(await evaluator.GetAsync(Url))).Items);
    }
}
