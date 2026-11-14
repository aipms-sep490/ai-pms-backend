using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class AcademicTrackingEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string Project(long id) => $"/api/v1/projects/{id}";
    private static string Evidence(long id) => $"/api/v1/project-evidence/{id}";

    [Fact]
    public async Task Lead_staff_override_requires_reason_token_and_audits_previous_decision()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var gate = await AddGate(x.S);
        var url = $"/api/v1/projects/{x.S.ProjectId}/review-gates/{gate.Code}/override";
        var reason = "Căn cứ biên bản họp khoa, hội đồng thống nhất chấp nhận toàn bộ minh chứng bổ sung của đề tài.";
        var request = new OverrideReviewGateRequest("PASSED", reason, gate.ConcurrencyToken.ToString("N"));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(url, request with { Reason = "too short" })).StatusCode);
        var result = await Body<OverrideReviewGateDto>(await staff.PostAsJsonAsync(url, request));
        Assert.Equal("OVERRIDE", result.DecisionSource); Assert.Equal("PASSED", result.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url, request)).StatusCode);
        var capabilities = await Body<ReviewGateUserCapabilitiesDto>(
            await staff.GetAsync($"/api/v1/users/me/capabilities?projectId={x.S.ProjectId}"));
        Assert.True(capabilities.Actions.Single(a => a.Code == "CAN_OVERRIDE_GATE").Allowed);
        await using var db = database.CreateContext();
        Assert.Equal("OVERRIDE", (await db.ProjectGateInstances.SingleAsync(g => g.Id == gate.Id)).DecisionSource);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "REVIEW_GATE_OVERRIDE" && a.EntityId == gate.Id.ToString()));
    }

    [Fact]
    public async Task Gate_script_requires_chair_or_secretary_and_sync_cannot_replay_stale_token()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        var gate = await AddGate(x.S);
        using var chair = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var roomKey = "gate-" + gate.Id;
        long videoSessionId;
        await using (var db = database.CreateContext())
        {
            db.GateReviewerAssignments.Add(new GateReviewerAssignment { GateInstanceId = gate.Id,
                ReviewerId = x.S.Scope.Users.NewLecturer, Scope = "COMMON", SessionRole = "CHAIR", CreatedAt = Now });
            var meeting = new M.Meeting { ProjectId = x.S.ProjectId, Title = "Review gate session",
                StartAt = Now.AddHours(-1), EndAt = Now.AddMinutes(15),
                Status = "SCHEDULED", CreatedBy = x.S.Scope.Users.Staff,
                CreatedAt = Now, UpdatedAt = Now };
            db.Meetings.Add(meeting); await db.SaveChangesAsync();
            var video = new M.MeetingVideoSession { MeetingId = meeting.Id, Provider = "LIVEKIT",
                ProviderRoomKey = roomKey, Status = "LIVE", StartedAt = Now.AddMinutes(-30),
                CreatedAt = Now.AddHours(-1), UpdatedAt = Now };
            db.MeetingVideoSessions.Add(video); await db.SaveChangesAsync(); videoSessionId = video.Id;
            db.MeetingVideoPresenceSessions.Add(new M.MeetingVideoPresenceSession {
                MeetingVideoSessionId = video.Id, UserId = x.S.Scope.Users.NewLecturer,
                ProviderParticipantIdentity = "test-chair", JoinedAt = Now.AddMinutes(-20),
                CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }
        var start = new DateTimeOffset(Now).AddHours(-1); var end = start.AddMinutes(75);
        var url = $"/api/v1/review-gates/{gate.Id}/session-script";
        var input = new SaveGateSessionScriptRequest("ONLINE", null, roomKey,
            start, end, null, null, [], [new GateAgendaStage("Questions", 30, "PENDING")],
            [], [], "Initial agenda", "PENDING", null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PutAsJsonAsync(url, input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await chair.PutAsJsonAsync(url,
            input with { Mode = "OFFLINE", RoomId = 1, LiveKitRoomName = null })).StatusCode);
        var saved = await Body<GateSessionScriptDto>(await chair.PutAsJsonAsync(url, input));
        Assert.Equal("ONLINE", saved.Mode); Assert.Single(saved.AgendaStages);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsJsonAsync(url, input)).StatusCode);
        var presence = new SyncGatePresenceRequest(videoSessionId.ToString(),
            [new LiveKitPresenceParticipant(x.S.Scope.Users.NewLecturer,
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), null, 20)], saved.ConcurrencyToken);
        var synced = await Body<GateSessionScriptDto>(await chair.PostAsJsonAsync(
            $"/api/v1/review-gates/{gate.Id}/livekit-presence/sync", presence));
        Assert.Single(synced.Presences);
        Assert.True(synced.Presences.Single().JoinedAt > new DateTimeOffset(Now).AddHours(-1));
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PostAsJsonAsync(
            $"/api/v1/review-gates/{gate.Id}/livekit-presence/sync", presence)).StatusCode);
        var readback = await Body<GateSessionScriptDto>(await chair.GetAsync(url));
        Assert.Equal(synced.ConcurrencyToken, readback.ConcurrencyToken);
    }

    [Fact]
    public async Task Offline_gate_script_uses_real_roster_and_period_working_days()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        var gate = await AddGate(x.S, 3);
        using var chair = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        using var secretary = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        long roomId; string scriptToken;
        await using (var db = database.CreateContext())
        {
            db.GateReviewerAssignments.Add(new GateReviewerAssignment { GateInstanceId = gate.Id,
                ReviewerId = x.S.Scope.Users.NewLecturer, Scope = "COMMON", SessionRole = "CHAIR", CreatedAt = Now });
            db.GateReviewerAssignments.Add(new GateReviewerAssignment { GateInstanceId = gate.Id,
                ReviewerId = x.S.Scope.Users.Staff, Scope = "COMMON", SessionRole = "SECRETARY", CreatedAt = Now });
            var room = new Room { Code = "GATE-" + Guid.NewGuid().ToString("N")[..8],
                Building = "Test", Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            db.Add(room);
            db.Set<ProjectPeriodCalendar>().Add(new ProjectPeriodCalendar { ProjectPeriodId = x.S.PeriodId,
                WorkingDaysOfWeek = 62, HolidaysJson = "[]", CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync(); roomId = room.Id;
            var script = new GateSessionScript { ProjectGateInstanceId = gate.Id, Mode = "OFFLINE",
                RoomId = room.Id, ScheduledStartAt = new DateTimeOffset(Now),
                ScheduledEndAt = new DateTimeOffset(Now.AddMinutes(75)),
                PublishedAt = new DateTimeOffset(Now), PublishedBy = x.S.Scope.Users.Staff,
                CreatedAt = new DateTimeOffset(Now), UpdatedAt = new DateTimeOffset(Now) };
            db.Add(script); await db.SaveChangesAsync(); scriptToken = script.ConcurrencyToken.ToString("N");
        }
        var scheduled = new DateTimeOffset(Now);
        var remediation = new GateRemediationItem("fix-1", "Revise architecture", "Team",
            new DateOnly(2026, 9, 11), true, 5);
        var input = new SaveGateSessionScriptRequest("OFFLINE", roomId, null,
            scheduled, scheduled.AddMinutes(75), null, null,
            [new GatePresence(x.S.Scope.Users.Student, "Forged name", "CHAIR", true)],
            [], [], [remediation], null, "PENDING", null, null, scriptToken);
        var url = $"/api/v1/review-gates/{gate.Id}/session-script";
        Assert.Equal(HttpStatusCode.Forbidden, (await chair.PutAsJsonAsync(url,
            input with { Presences = [new GatePresence(x.S.Scope.Users.Admin, "Admin", "CHAIR", true)] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await chair.PutAsJsonAsync(url,
            input with { SignedBySecretary = "Staff" })).StatusCode);
        var saved = await Body<GateSessionScriptDto>(await chair.PutAsJsonAsync(url,
            input with { SignedByChair = "NewLecturer" }));
        Assert.Equal("STUDENT", Assert.Single(saved.Presences).Role);
        Assert.Equal("Student", Assert.Single(saved.Presences).Name);
        Assert.Equal(new DateOnly(2026, 9, 17), Assert.Single(saved.RemediationItems).DueDate);
        var final = await Body<GateSessionScriptDto>(await secretary.PutAsJsonAsync(url,
            input with { SignedByChair = "NewLecturer", SignedBySecretary = "Staff",
                Verdict = "PASSED", ConcurrencyToken = saved.ConcurrencyToken }));
        Assert.Equal("PASSED", final.Verdict);
    }

    [Fact]
    public async Task Offline_gate_script_cannot_take_a_room_already_booked_for_defense()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        var gate = await AddGate(x.S, 3);
        using var chair = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        long roomId;
        var slot = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 1);
        await using (var db = database.CreateContext())
        {
            var room = new Room { Code = "SHARED-" + Guid.NewGuid().ToString("N")[..8],
                Building = "Test", Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Defense",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now };
            db.AddRange(room, committee); await db.SaveChangesAsync(); roomId = room.Id;
            db.Add(new DefenseSession { ProjectId = x.S.ProjectId, CommitteeId = committee.Id,
                RoomId = room.Id, Room = room.Code, StartAt = slot.Start.UtcDateTime,
                EndAt = slot.End.UtcDateTime, CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now });
            db.GateReviewerAssignments.Add(new GateReviewerAssignment { GateInstanceId = gate.Id,
                ReviewerId = x.S.Scope.Users.NewLecturer, Scope = "COMMON", SessionRole = "CHAIR", CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        var request = new SaveGateSessionScriptRequest("OFFLINE", roomId, null,
            slot.Start, slot.End, null, null, [], [], [], [], null, "PENDING", null, null, null);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsJsonAsync(
            $"/api/v1/review-gates/{gate.Id}/session-script", request)).StatusCode);
    }

    private async Task<ProjectGateInstance> AddGate(EvaluationScenario scenario, int sequence = 1)
    {
        await using var db = database.CreateContext();
        var template = new ReviewGateTemplate { ProjectPeriodId = scenario.PeriodId,
            Code = "GATE-" + Guid.NewGuid().ToString("N")[..8], Name = "Gate script test",
            Mode = "SINGLE_MAJOR", Major = "COMMON", Status = "PUBLISHED", Version = 1,
            CreatedBy = scenario.Scope.Users.Staff, CreatedAt = Now, UpdatedAt = Now };
        db.ReviewGateTemplates.Add(template); await db.SaveChangesAsync();
        var gate = new ProjectGateInstance { ProjectId = scenario.ProjectId, TemplateId = template.Id,
            TemplateVersion = 1, Code = "R" + Guid.NewGuid().ToString("N")[..4], Sequence = sequence,
            Title = "Review gate", Objective = "Test review", Status = "IN_REVIEW",
            ReviewEligibility = "READY", CreatedAt = Now,
            WindowStart = sequence == 3 ? Now.AddDays(-1) : null,
            WindowEnd = sequence == 3 ? Now.AddDays(10) : null };
        db.ProjectGateInstances.Add(gate); await db.SaveChangesAsync();
        if (sequence == 3)
        {
            db.ProjectGateInstances.Add(new ProjectGateInstance { ProjectId = scenario.ProjectId, TemplateId = template.Id,
                TemplateVersion = 1, Code = "R2", Sequence = 2, Title = "Review 2", Status = "PASSED",
                ReviewEligibility = "READY", CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        return gate;
    }

    private async Task<ProjectGateInstance> AddSchedulingGates(EvaluationScenario scenario, string r3Status = "PASSED")
    {
        await using var db = database.CreateContext();
        var template = new ReviewGateTemplate { ProjectPeriodId = scenario.PeriodId,
            Code = "SCHED-" + Guid.NewGuid().ToString("N")[..8], Name = "Scheduling gates",
            Mode = "SINGLE_MAJOR", Major = "COMMON", Status = "PUBLISHED", Version = 1,
            CreatedBy = scenario.Scope.Users.Staff, CreatedAt = Now, UpdatedAt = Now };
        db.ReviewGateTemplates.Add(template); await db.SaveChangesAsync();
        var start = Now.AddDays(-1);
        var end = Now.AddDays(10);
        var r3 = new ProjectGateInstance { ProjectId = scenario.ProjectId, TemplateId = template.Id,
                TemplateVersion = 1, Code = "R3", Sequence = 3, Title = "Review 3", Status = r3Status,
                ReviewEligibility = "READY", CreatedAt = Now };
        db.ProjectGateInstances.AddRange(r3,
            new ProjectGateInstance { ProjectId = scenario.ProjectId, TemplateId = template.Id,
                TemplateVersion = 1, Code = "R4", Sequence = 4, Title = "Defense", Status = "PLANNED",
                ReviewEligibility = r3Status == "PASSED" ? "READY" : "BLOCKED",
                WindowStart = start, WindowEnd = end, CreatedAt = Now });
        await db.SaveChangesAsync();
        return r3;
    }

    [Fact]
    public async Task Room_catalog_period_timetable_and_availability_use_stable_room_ids()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var roomCode = "CAT-" + Guid.NewGuid().ToString("N")[..8];
        var room = await Body<RoomDto>(await staff.PostAsJsonAsync("/api/v1/rooms",
            new SaveRoomRequest(roomCode, "Test Building", 2, 40, "ACTIVE")));
        var start = Now.AddDays(2); var end = start.AddMinutes(75);
        await using (var db = database.CreateContext())
        {
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Catalog committee",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now };
            db.Add(committee); await db.SaveChangesAsync();
            db.Add(new DefenseSession { ProjectId = x.S.ProjectId, CommitteeId = committee.Id,
                RoomId = room.Id, Room = room.Code, StartAt = start, EndAt = end,
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        var local = new DateTimeOffset(start).ToOffset(TimeSpan.FromHours(7));
        var date = local.ToString("yyyy-MM-dd");
        var timetable = await Body<PeriodDefenseSessionDto[]>(await staff.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions?date={date}"));
        Assert.Contains(timetable, s => s.ProjectId == x.S.ProjectId && s.RoomId == room.Id && s.RoomCode == room.Code);
        var availability = await Body<RoomDto[]>(await staff.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/rooms/availability?date={date}&startAt={local:HH:mm}&endAt={local.AddMinutes(75):HH:mm}"));
        Assert.DoesNotContain(availability, r => r.Id == room.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.DeleteAsync(
            $"/api/v1/rooms/{room.Id}?concurrencyToken={room.ConcurrencyToken}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync($"/api/v1/rooms/{room.Id}",
            new SaveRoomRequest(room.Code, room.Building, room.Floor, room.Capacity, room.Status, Guid.NewGuid().ToString("N")))).StatusCode);
    }

    [Fact]
    public async Task Inactive_staff_department_cannot_read_rooms_or_manage_period_schedules()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        await using (var db = database.CreateContext())
        {
            var departmentId = await db.Users.Where(u => u.Id == x.S.Scope.Users.Staff)
                .Select(u => u.DepartmentId).SingleAsync();
            (await db.Departments.SingleAsync(d => d.Id == departmentId)).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/project-periods/{x.S.PeriodId}/calendar")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/project-periods/{x.S.PeriodId}/review-gates/r3/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/change-requests")).StatusCode);
        var request = new AutoSchedulePreviewRequest(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14), [1]);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/auto-schedule/preview", request)).StatusCode);
    }

    [Fact]
    public async Task Auto_schedule_preview_is_read_only_and_apply_rechecks_revision_under_lock()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        await AddSchedulingGates(x.S);
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        long roomId;
        await using (var db = database.CreateContext())
        {
            var room = new Room { Code = "AUTO-" + Guid.NewGuid().ToString("N")[..8], Building = "Test",
                Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Auto committee",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now,
                Members = [new DefenseCommitteeMember { UserId = x.S.Scope.Users.NewLecturer, MajorId = x.Major }] };
            db.AddRange(room, committee); await db.SaveChangesAsync(); roomId = room.Id;
        }
        var url = $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/auto-schedule";
        var request = new AutoSchedulePreviewRequest(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14), [roomId]);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url + "/preview", request)).StatusCode);
        var blockedRoom = request with { Exclusions = [new RoomSchedulingExclusion(roomId, null, null)] };
        var noRoom = await Body<AutoSchedulePreviewDto>(await staff.PostAsJsonAsync(url + "/preview", blockedRoom));
        Assert.Empty(noRoom.Allocations);
        Assert.Contains(noRoom.Conflicts, c => c.ProjectId == x.S.ProjectId && c.Reason == "NO_ROOM_OR_CONFLICT_FREE_SLOT");
        var blockedSlot = request with { Exclusions = [new RoomSchedulingExclusion(roomId, request.StartDate, 1)] };
        var nextSlot = await Body<AutoSchedulePreviewDto>(await staff.PostAsJsonAsync(url + "/preview", blockedSlot));
        Assert.Single(nextSlot.Allocations);
        Assert.Equal(2, nextSlot.Allocations.Single().SlotNumber);
        var preview = await Body<AutoSchedulePreviewDto>(await staff.PostAsJsonAsync(url + "/preview", request));
        Assert.Single(preview.Allocations, a => a.ProjectId == x.S.ProjectId && a.RoomId == roomId);
        var adminPreview = await Body<AutoSchedulePreviewDto>(await admin.PostAsJsonAsync(url + "/preview", request));
        Assert.Single(adminPreview.Allocations, a => a.ProjectId == x.S.ProjectId && a.RoomId == roomId);
        await using (var db = database.CreateContext())
            Assert.False(await db.Set<DefenseSession>().AnyAsync(s => s.ProjectId == x.S.ProjectId));
        var apply = new AutoScheduleApplyRequest(preview.Allocations, preview.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/apply",
            apply with { Exclusions = [new RoomSchedulingExclusion(roomId, request.StartDate, 1)] })).StatusCode);
        var result = await Body<AutoScheduleApplyDto>(await admin.PostAsJsonAsync(url + "/apply", apply));
        Assert.True(result.Success); Assert.Equal(1, result.CreatedSessionsCount);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(url + "/apply", apply)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var scheduledSession = await db.Set<DefenseSession>().SingleAsync(s => s.ProjectId == x.S.ProjectId && s.RoomId == roomId);
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.NotificationType == "DEFENSE_SCHEDULED"
                && n.RelatedEntityType == "DEFENSE_SESSION" && n.RelatedEntityId == scheduledSession.Id));
        }
    }

    [Fact]
    public async Task Auto_schedule_avoids_room_and_reviewer_occupied_by_offline_gate()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        var gate = await AddSchedulingGates(x.S);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        long roomId;
        await using (var db = database.CreateContext())
        {
            var room = new Room { Code = "GATE-BOOK-" + Guid.NewGuid().ToString("N")[..8],
                Building = "Test", Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Defense committee",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now,
                Members = [new DefenseCommitteeMember { UserId = x.S.Scope.Users.NewLecturer, MajorId = x.Major }] };
            var slot = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 1);
            db.AddRange(room, committee); await db.SaveChangesAsync(); roomId = room.Id;
            db.GateReviewerAssignments.Add(new GateReviewerAssignment { GateInstanceId = gate.Id,
                ReviewerId = x.S.Scope.Users.NewLecturer, Scope = "COMMON", CreatedAt = Now });
            db.Set<GateSessionScript>().Add(new GateSessionScript { ProjectGateInstanceId = gate.Id,
                Mode = "OFFLINE", RoomId = room.Id, ScheduledStartAt = slot.Start,
                ScheduledEndAt = slot.End, CreatedAt = slot.Start, UpdatedAt = slot.Start });
            await db.SaveChangesAsync();
        }
        var url = $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/auto-schedule/preview";
        var preview = await Body<AutoSchedulePreviewDto>(await admin.PostAsJsonAsync(url,
            new AutoSchedulePreviewRequest(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14), [roomId])));
        Assert.Equal(2, Assert.Single(preview.Allocations).SlotNumber);
        var available = await Body<RoomDto[]>(await admin.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/rooms/availability?date=2026-09-14&startAt=08:00&endAt=09:15"));
        Assert.DoesNotContain(available, room => room.Id == roomId);
    }

    [Fact]
    public async Task Auto_schedule_requires_passed_R3_and_a_configured_R4_window()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        await AddSchedulingGates(x.S, "REVISION_REQUIRED");
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        long roomId;
        await using (var db = database.CreateContext())
        {
            var room = new Room { Code = "ELIG-" + Guid.NewGuid().ToString("N")[..8], Building = "Test",
                Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Defense",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now,
                Members = [new DefenseCommitteeMember { UserId = x.S.Scope.Users.NewLecturer, MajorId = x.Major }] };
            db.AddRange(room, committee); await db.SaveChangesAsync(); roomId = room.Id;
        }
        var url = $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/auto-schedule/preview";
        var input = new AutoSchedulePreviewRequest(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14), [roomId]);
        var blocked = await Body<AutoSchedulePreviewDto>(await admin.PostAsJsonAsync(url, input));
        Assert.Empty(blocked.Allocations);
        Assert.Contains(blocked.Conflicts, c => c.ProjectId == x.S.ProjectId && c.Reason == "REVIEW_GATE_3_NOT_PASSED");
        await using (var db = database.CreateContext())
        {
            var r3 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == x.S.ProjectId && g.Sequence == 3);
            r3.Status = "PASSED"; r3.ConcurrencyToken = Guid.NewGuid();
            var r4 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == x.S.ProjectId && g.Sequence == 4);
            r4.WindowEnd = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 1).End.UtcDateTime;
            r4.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        var limited = await Body<AutoSchedulePreviewDto>(await admin.PostAsJsonAsync(url, input));
        Assert.Single(limited.Allocations);
        Assert.Equal(1, limited.Allocations.Single().SlotNumber);
    }

    [Fact]
    public async Task Gate3_auto_schedule_requires_R2_and_publishes_then_admin_changes_with_notification()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        var r3 = await AddSchedulingGates(x.S, "PLANNED");
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        using var chair = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        long roomId, lockedRoomId;
        await using (var db = database.CreateContext())
        {
            var r2 = new ProjectGateInstance { ProjectId = x.S.ProjectId, TemplateId = r3.TemplateId,
                TemplateVersion = 1, Code = "R2", Sequence = 2, Title = "Review 2",
                Status = "REVISION_REQUIRED", ReviewEligibility = "READY", CreatedAt = Now };
            var gate = await db.ProjectGateInstances.SingleAsync(g => g.Id == r3.Id);
            gate.WindowStart = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 1).Start.UtcDateTime;
            gate.WindowEnd = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 6).End.UtcDateTime;
            var room = new Room { Code = "R3-" + Guid.NewGuid().ToString("N")[..8],
                Building = "Test", Floor = 2, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var lockedRoom = new Room { Code = "R3-LOCK-" + Guid.NewGuid().ToString("N")[..8],
                Building = "Test", Floor = 3, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            db.AddRange(r2, room, lockedRoom);
            db.GateReviewerAssignments.AddRange(
                new GateReviewerAssignment { GateInstanceId = r3.Id, ReviewerId = x.S.Scope.Users.NewLecturer,
                    Scope = "COMMON", SessionRole = "CHAIR", CreatedAt = Now },
                new GateReviewerAssignment { GateInstanceId = r3.Id, ReviewerId = x.S.Scope.Users.Staff,
                    Scope = "COMMON", SessionRole = "SECRETARY", CreatedAt = Now });
            await db.SaveChangesAsync(); roomId = room.Id; lockedRoomId = lockedRoom.Id;
        }
        var url = $"/api/v1/project-periods/{x.S.PeriodId}/review-gates/r3/schedule";
        var request = new AutoSchedulePreviewRequest(new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 14),
            [roomId, lockedRoomId]);
        var firstSlot = DefenseSchedulingRules.Slot(request.StartDate, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsJsonAsync(
            $"/api/v1/review-gates/{r3.Id}/session-script",
            new SaveGateSessionScriptRequest("OFFLINE", roomId, null, firstSlot.Start, firstSlot.End,
                null, null, [], [], [], [], null, "PENDING", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url + "/preview", request)).StatusCode);
        var blocked = await Body<ReviewGate3SchedulePreviewDto>(await admin.PostAsJsonAsync(url + "/preview", request));
        Assert.Empty(blocked.Allocations);
        Assert.Contains(blocked.Conflicts, c => c.ProjectId == x.S.ProjectId && c.Reason == "REVIEW_GATE_2_NOT_PASSED");
        await using (var db = database.CreateContext())
        {
            var r2 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == x.S.ProjectId && g.Sequence == 2);
            r2.Status = "PASSED"; r2.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsJsonAsync(
            $"/api/v1/review-gates/{r3.Id}/session-script",
            new SaveGateSessionScriptRequest("OFFLINE", roomId, null, firstSlot.Start, firstSlot.End,
                null, null, [], [], [], [], null, "PENDING", null, null, null))).StatusCode);
        var excluded = request with { Exclusions = [new RoomSchedulingExclusion(roomId, request.StartDate, 1),
            new RoomSchedulingExclusion(lockedRoomId, null, null)] };
        var next = await Body<ReviewGate3SchedulePreviewDto>(await admin.PostAsJsonAsync(url + "/preview", excluded));
        Assert.Equal(2, Assert.Single(next.Allocations).SlotNumber);
        var preview = await Body<ReviewGate3SchedulePreviewDto>(await admin.PostAsJsonAsync(url + "/preview", request));
        Assert.Equal(1, Assert.Single(preview.Allocations).SlotNumber);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(url + "/apply",
            new ReviewGate3ScheduleApplyRequest(preview.Allocations, preview.ConcurrencyToken,
                [new RoomSchedulingExclusion(roomId, request.StartDate, 1)]))).StatusCode);
        var published = await Body<ReviewGate3ScheduleDto[]>(await admin.PostAsJsonAsync(url + "/apply",
            new ReviewGate3ScheduleApplyRequest(preview.Allocations, preview.ConcurrencyToken,
                [new RoomSchedulingExclusion(lockedRoomId, null, null)])));
        var schedule = Assert.Single(published);
        Assert.Equal(roomId, schedule.RoomId);
        Assert.Single(await Body<ReviewGate3ScheduleDto[]>(await admin.GetAsync(url)));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(url + "/apply",
            new ReviewGate3ScheduleApplyRequest(preview.Allocations, preview.ConcurrencyToken))).StatusCode);
        var scriptUrl = $"/api/v1/review-gates/{r3.Id}/session-script";
        var studentView = await Body<GateSessionScriptDto>(await student.GetAsync(scriptUrl));
        Assert.Equal(roomId, studentView.RoomId);
        Assert.NotNull(studentView.PublishedAt);
        var edit = new SaveGateSessionScriptRequest("OFFLINE", roomId, null,
            DefenseSchedulingRules.Slot(request.StartDate, 2).Start,
            DefenseSchedulingRules.Slot(request.StartDate, 2).End,
            null, null, [], [], [], [], null, "PENDING", null, null, studentView.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PutAsJsonAsync(scriptUrl, edit)).StatusCode);
        var slot2 = DefenseSchedulingRules.Slot(request.StartDate, 2);
        var changed = await Body<ReviewGate3ScheduleDto>(await admin.PutAsJsonAsync(url + "/" + r3.Id,
            new ReviewGate3ScheduleChangeRequest(roomId, slot2.Start, slot2.End,
                "Move the review to a later office slot", schedule.ConcurrencyToken)));
        Assert.Equal(slot2.Start, changed.StartAt);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(url + "/" + r3.Id,
            new ReviewGate3ScheduleChangeRequest(roomId, slot2.Start, slot2.End,
                "Replay old token", schedule.ConcurrencyToken))).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal(2, await verify.Notifications.CountAsync(n => n.RelatedEntityType == "GATE_SESSION_SCRIPT"
            && n.RelatedEntityId == studentView.Id));
    }

    [Fact]
    public async Task Chair_can_request_change_lead_staff_decides_and_token_prevents_replay()
    {
        using var app = new TrackingFactory(database); var x = await Prepare();
        await AddSchedulingGates(x.S);
        using var chair = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        using var staff = app.CreateAuthenticatedClient(x.S.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(x.S.Scope.Users.Admin);
        using var student = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var slot = DefenseSchedulingRules.Slot(new DateOnly(2026, 9, 14), 1);
        long sessionId, desiredRoomId, committeeId; string committeeToken;
        await using (var db = database.CreateContext())
        {
            var currentRoom = new Room { Code = "CUR-" + Guid.NewGuid().ToString("N")[..8], Building = "Test",
                Floor = 1, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var desiredRoom = new Room { Code = "NEW-" + Guid.NewGuid().ToString("N")[..8], Building = "Test",
                Floor = 1, Capacity = 40, CreatedAt = Now, UpdatedAt = Now };
            var committee = new DefenseCommittee { ProjectId = x.S.ProjectId, Name = "Chair committee",
                CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now,
                Members = [new DefenseCommitteeMember { UserId = x.S.Scope.Users.NewLecturer, MajorId = x.Major }] };
            db.AddRange(currentRoom, desiredRoom, committee); await db.SaveChangesAsync();
            committeeId = committee.Id; committeeToken = committee.ConcurrencyToken.ToString("N");
            var session = new DefenseSession { ProjectId = x.S.ProjectId, CommitteeId = committee.Id,
                RoomId = currentRoom.Id, Room = currentRoom.Code, StartAt = slot.Start.UtcDateTime,
                EndAt = slot.End.UtcDateTime, CreatedBy = x.S.Scope.Users.Staff, CreatedAt = Now };
            db.Add(session); await db.SaveChangesAsync();
            sessionId = session.Id; desiredRoomId = desiredRoom.Id;
        }
        var assigned = await Body<CommitteeDto>(await staff.PutAsJsonAsync(
            $"/api/v1/defense-committees/{committeeId}/roles",
            new SaveCommitteeRolesRequest([new CommitteeMemberInput(x.S.Scope.Users.NewLecturer, x.Major, "CHAIR")], committeeToken)));
        Assert.Equal("CHAIR", Assert.Single(assigned.Members).Role);
        var url = $"/api/v1/defense-sessions/{sessionId}/change-requests";
        var request = new CreateDefenseChangeRequest(desiredRoomId, slot.Start, slot.End,
            "ROOM_FACILITY_ISSUE", "The projector is unavailable in the original room.", null);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.PostAsJsonAsync(url, request)).StatusCode);
        var pending = await Body<DefenseChangeRequestDto>(await chair.PostAsJsonAsync(url, request));
        Assert.Equal("PENDING", pending.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PostAsJsonAsync(url, request)).StatusCode);
        var decisionUrl = $"/api/v1/defense-sessions/change-requests/{pending.Id}/decision";
        var decision = new DecideDefenseChangeRequest("APPROVED", "Approved by lead staff", pending.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await chair.PostAsJsonAsync(decisionUrl, decision)).StatusCode);
        var approved = await Body<DefenseChangeRequestDto>(await staff.PostAsJsonAsync(decisionUrl, decision));
        Assert.Equal("APPROVED", approved.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(decisionUrl, decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await chair.PostAsJsonAsync(
            $"/api/v1/defense-sessions/change-requests/{pending.Id}/withdraw",
            new WithdrawDefenseChangeRequest(pending.ConcurrencyToken))).StatusCode);
        var second = await Body<DefenseChangeRequestDto>(await chair.PostAsJsonAsync(url,
            request with { DesiredRoomId = pending.CurrentRoomId }));
        var withdrawn = await Body<DefenseChangeRequestDto>(await chair.PostAsJsonAsync(
            $"/api/v1/defense-sessions/change-requests/{second.Id}/withdraw",
            new WithdrawDefenseChangeRequest(second.ConcurrencyToken)));
        Assert.Equal("WITHDRAWN", withdrawn.Status);
        var adminList = await Body<DefenseChangeRequestDto[]>(await admin.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/change-requests?status=WITHDRAWN"));
        Assert.Contains(adminList, r => r.Id == withdrawn.Id);
        var third = await Body<DefenseChangeRequestDto>(await chair.PostAsJsonAsync(url,
            request with { DesiredRoomId = pending.CurrentRoomId }));
        var adminApproved = await Body<DefenseChangeRequestDto>(await admin.PostAsJsonAsync(
            $"/api/v1/defense-sessions/change-requests/{third.Id}/decision",
            new DecideDefenseChangeRequest("APPROVED", "Admin restored the original room", third.ConcurrencyToken)));
        Assert.Equal("APPROVED", adminApproved.Status);
        var pendingList = await Body<DefenseChangeRequestDto[]>(await staff.GetAsync(
            $"/api/v1/project-periods/{x.S.PeriodId}/defense-sessions/change-requests?status=PENDING"));
        Assert.DoesNotContain(pendingList, r => r.Id == withdrawn.Id);
        await using var verify = database.CreateContext();
        Assert.Equal(pending.CurrentRoomId, (await verify.Set<DefenseSession>().FindAsync(sessionId))!.RoomId);
        Assert.Equal(2, await verify.Notifications.CountAsync(n => n.NotificationType == "DEFENSE_SCHEDULE_CHANGED"
            && n.RelatedEntityId == sessionId));
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.Action == "DEFENSE_CHANGE_APPROVED" && a.EntityId == pending.Id.ToString()));
    }
    private async Task<(EvaluationScenario S, long Primary, long Major, long Evidence, long Task)> Prepare()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext();
        var assignment = await db.SupervisorAssignments.Include(a => a.SupervisorProfile).SingleAsync(a => a.ProjectId == s.ProjectId);
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var task = new M.Task { Title = "Synthetic task", Status = "TODO", CreatedBy = s.Scope.Users.Student,
            Milestone = new() { ProjectId = s.ProjectId, Title = "Synthetic milestone", Status = "PLANNED", CreatedBy = s.Scope.Users.Student } };
        db.Tasks.Add(task); await db.SaveChangesAsync();
        db.TaskDisciplines.Add(new() { TaskId = task.Id, MajorId = major, Role = "PRIMARY", CreatedBy = s.Scope.Users.Student, CreatedAt = Now });
        var evidence = new M.ProjectEvidence { ProjectId = s.ProjectId, MajorId = major, SourceType = "TASK", SourceId = task.Id,
            TaskId = task.Id, VerificationStatus = "PENDING", SubmittedBy = s.Scope.Users.Student, SubmittedAt = Now };
        db.ProjectEvidence.Add(evidence); await db.SaveChangesAsync();
        return (s, assignment.SupervisorProfile.UserId, major, evidence.Id, task.Id);
    }
    private static ReviewEvidenceRequest Review(EvidenceReviewTarget target, string decision = "ACCEPTED") => new(target.SourceVersionId,
        target.SourceVersion, target.LatestReviewId, decision, "Synthetic reason");

    [Fact]
    public async Task Exact_source_review_is_append_only_stale_safe_and_checkpoint_never_changes_project_state()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        Assert.True(target.CanReview);
        await using (var db = database.CreateContext()) { var task = (await db.Tasks.FindAsync(x.Task))!; task.Description = "New version"; task.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        var review = await Body<EvidenceReviewDto>(await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target)));
        Assert.Equal(target.SourceVersion, review.SourceVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        var checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Demo gate", x.Major, new(Now.AddDays(-1)))));
        Assert.True(checkpoint.IsOverdue); Assert.True(checkpoint.TrackingOnly); Assert.False(checkpoint.BlocksAcademicWorkflow);
        var decision = new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "NOT_MET", "Tracking signal only", [new(x.Evidence, review.Id)]);
        checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync($"/api/v1/checkpoints/{checkpoint.Id}/decisions", decision));
        Assert.Single(checkpoint.Decisions); Assert.True(checkpoint.IsOverdue);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync($"/api/v1/checkpoints/{checkpoint.Id}/decisions", decision)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PutAsJsonAsync(Project(x.S.ProjectId) + $"/checkpoints/{checkpoint.Id}", new SaveCheckpointRequest("Rewrite", x.Major, new(Now), checkpoint.ConcurrencyToken))).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Equal("FINAL_SUBMISSION", (await verify.Projects.FindAsync(x.S.ProjectId))!.Status);
        Assert.Equal("VERIFIED", (await verify.ProjectEvidence.FindAsync(x.Evidence))!.VerificationStatus);
        Assert.Single(await verify.Set<ProjectEvidenceReview>().Where(r => r.EvidenceId == x.Evidence).ToArrayAsync());
        Assert.True(await verify.AuditLogs.AnyAsync(a => a.Action == "CHECKPOINT_DECISION_RECORDED" && a.EntityId == checkpoint.Id.ToString()));
        var history = await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"));
        Assert.Equal(review.SourceVersion, history.Items.Single().SourceVersion);
        Assert.DoesNotContain("sourceSnapshotJson", JsonSerializer.Serialize(history), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scoped_mentor_review_ended_assignment_admin_and_archived_write_denials()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        long mentorAssignment;
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = x.S.Scope.Users.NewLecturer };
            var request = new M.SupervisorRequest { ProjectId = x.S.ProjectId, SupervisorProfile = profile, RequestedBy = x.S.Scope.Users.Student,
                Status = "ACCEPTED", RequestedAt = Now.AddDays(-1), RespondedAt = Now };
            var assignment = new M.SupervisorAssignment { ProjectId = x.S.ProjectId, SupervisorRequest = request, SupervisorProfile = profile,
                AssignmentType = "DISCIPLINE_MENTOR", MajorId = x.Major, IsPrimary = false, AssignedAt = Now };
            db.Add(assignment); await db.SaveChangesAsync(); mentorAssignment = assignment.Id;
        }
        using var mentor = app.CreateAuthenticatedClient(x.S.Scope.Users.NewLecturer);
        var target = await Body<EvidenceReviewTarget>(await mentor.GetAsync(Evidence(x.Evidence) + "/review-target"));
        await Body<EvidenceReviewDto>(await mentor.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target)));
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Common", null, new(Now)))).StatusCode);
        await Body<CheckpointDto>(await mentor.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Major", x.Major, new(Now))));
        foreach (var user in new[] { x.S.Scope.Users.Admin, x.S.Scope.Users.OtherLecturer, x.S.Scope.Users.Student, x.S.Scope.Users.Staff })
        {
            using var outsider = app.CreateAuthenticatedClient(user);
            Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        }
        await using (var db = database.CreateContext()) { (await db.SupervisorAssignments.FindAsync(mentorAssignment))!.EndedAt = Now.AddHours(1); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await mentor.GetAsync(Evidence(x.Evidence) + "/reviews")).StatusCode);
        target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        await using (var db = database.CreateContext()) { (await db.Projects.FindAsync(x.S.ProjectId))!.Status = "ARCHIVED"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        Assert.False((await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"))).CanReview);
    }

    [Fact]
    public async Task Concurrent_review_and_audit_failure_preserve_append_only_history()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); using var primary = app.CreateAuthenticatedClient(x.Primary);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(x.Evidence) + "/review-target"));
        app.FailAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))).StatusCode);
        Assert.Empty((await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"))).Items);
        app.FailAudit = false;
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => primary.PostAsJsonAsync(Evidence(x.Evidence) + "/reviews", Review(target))));
        Assert.Single(results.Where(r => r.IsSuccessStatusCode)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await database.Migrate(); await database.Migrate();
        Assert.Single((await Body<PagedResult<EvidenceReviewDto>>(await primary.GetAsync(Evidence(x.Evidence) + "/reviews"))).Items);
    }

    [Fact]
    public async Task Lead_schedules_qualified_committee_concurrent_room_booking_and_explicit_cancellation()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare();
        await AddSchedulingGates(a.S); await AddSchedulingGates(b.S);
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        using var admin = app.CreateAuthenticatedClient(a.S.Scope.Users.Admin);
        var inputA = new CreateCommitteeRequest("Synthetic committee", [new(a.Primary, a.Major)]);
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA)).StatusCode);
        await using (var db = database.CreateContext())
        {
            foreach (var item in new[] { a, b })
            {
                var profile = await db.SupervisorProfiles.SingleAsync(p => p.UserId == item.Primary); profile.IsAvailable = true;
                profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(item.Major))!.Code });
            }
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA)).StatusCode);
        var ca = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", inputA));
        var cb = await Body<CommitteeDto>(await staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Other", [new(b.Primary, b.Major)])));
        Assert.False(ca.GrantsEvaluatorAuthority);
        var room = "E2E-" + Guid.NewGuid().ToString("N");
        var scheduleA = new ScheduleDefenseRequest(ca.Id, new(Now.AddHours(1)), new(Now.AddHours(2)), room);
        await using (var db = database.CreateContext())
        {
            var r3 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == a.S.ProjectId && g.Sequence == 3);
            r3.Status = "REVISION_REQUIRED"; r3.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(
            Project(a.S.ProjectId) + "/defense-sessions", scheduleA)).StatusCode);
        await using (var db = database.CreateContext())
        {
            var r3 = await db.ProjectGateInstances.SingleAsync(g => g.ProjectId == a.S.ProjectId && g.Sequence == 3);
            r3.Status = "PASSED"; r3.ConcurrencyToken = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        var scheduleB = scheduleA with { CommitteeId = cb.Id, Room = " " + room.ToLowerInvariant() + " " };
        var results = await Task.WhenAll(staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", scheduleA), staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-sessions", scheduleB));
        Assert.Single(results.Where(r => r.IsSuccessStatusCode)); Assert.Single(results.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var session = await Body<DefenseSessionDto>(results.Single(r => r.IsSuccessStatusCode));
        var owner = session.ProjectId == a.S.ProjectId ? staffA : staffB;
        var other = session.ProjectId == a.S.ProjectId ? staffB : staffA;
        var cancel = new CancelDefenseRequest(session.ConcurrencyToken, "Schedule correction");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        app.FailAudit = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        app.FailAudit = false;
        session = await Body<DefenseSessionDto>(await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel));
        Assert.Equal("CANCELLED", session.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", cancel)).StatusCode);
        await using var verify = database.CreateContext();
        Assert.Empty(await verify.Set<EvaluationAssignment>().Where(e => e.ProjectId == a.S.ProjectId || e.ProjectId == b.S.ProjectId).ToArrayAsync());
        Assert.Equal("FINAL_SUBMISSION", (await verify.Projects.FindAsync(a.S.ProjectId))!.Status);
        var notifications = await verify.Notifications.Where(n => n.RelatedEntityType == "DEFENSE_SESSION" && n.RelatedEntityId == session.Id).ToArrayAsync();
        Assert.Equal(2, notifications.Length);
        var notificationIds = notifications.Select(n => n.Id).ToArray();
        Assert.Equal(4, await verify.Set<NotificationEmailDelivery>().CountAsync(d => notificationIds.Contains(d.NotificationRecipient.NotificationId) && d.Status == "PENDING"));
    }
    [Fact]
    public async Task Deliverable_review_pins_explicit_version_and_file_manifest_without_exposing_storage()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); var foreign = await Prepare();
        using var primary = app.CreateAuthenticatedClient(x.Primary); long evidenceId; long firstId; long secondId; long fileId;
        await using (var db = database.CreateContext())
        {
            var deliverable = new M.Deliverable { ProjectId = x.S.ProjectId, Title = "Targeted report", Status = "OPEN", CreatedBy = x.S.Scope.Users.Student,
                EvidenceScope = "MAJOR_SPECIFIC", TargetMajorId = x.Major };
            var first = new M.DeliverableVersion { Deliverable = deliverable, VersionNumber = 1, SubmittedBy = x.S.Scope.Users.Student, Status = "SUBMITTED", SubmittedAt = Now };
            var second = new M.DeliverableVersion { Deliverable = deliverable, VersionNumber = 2, SubmittedBy = x.S.Scope.Users.Student, Status = "SUBMITTED", SubmittedAt = Now };
            var file = new M.File { UploadedBy = x.S.Scope.Users.Student, DeliverableVersion = first, OriginalFileName = "report.pdf", MimeType = "application/pdf",
                StoragePath = "private/exact-v1", ChecksumSha256 = new string('a', 64), FileSizeBytes = 100 };
            db.AddRange(first, second, file); await db.SaveChangesAsync(); firstId = first.Id; secondId = second.Id; fileId = file.Id;
            var evidence = new M.ProjectEvidence { ProjectId = x.S.ProjectId, SourceType = "DELIVERABLE", SourceId = deliverable.Id, DeliverableId = deliverable.Id,
                MajorId = x.Major, SubmittedBy = x.S.Scope.Users.Student, SubmittedAt = Now, VerificationStatus = "PENDING" };
            db.Add(evidence); await db.SaveChangesAsync(); evidenceId = evidence.Id;
        }
        Assert.Equal(HttpStatusCode.NotFound, (await primary.GetAsync(Evidence(evidenceId) + "/review-target")).StatusCode);
        var target = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(evidenceId) + $"/review-target?sourceVersionId={firstId}"));
        var review = await Body<EvidenceReviewDto>(await primary.PostAsJsonAsync(Evidence(evidenceId) + "/reviews", Review(target)));
        Assert.Equal(firstId, review.SourceVersionId);
        await using (var db = database.CreateContext()) { (await db.Files.FindAsync(fileId))!.StoragePath = "private/replaced"; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await primary.PostAsJsonAsync(Evidence(evidenceId) + "/reviews", Review(target) with { ExpectedLatestReviewId = review.Id })).StatusCode);
        var secondTarget = await Body<EvidenceReviewTarget>(await primary.GetAsync(Evidence(evidenceId) + $"/review-target?sourceVersionId={secondId}"));
        Assert.NotEqual(target.SourceVersion, secondTarget.SourceVersion);
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync(Evidence(foreign.Evidence) + "/reviews")).StatusCode);
        var history = await primary.GetAsync(Evidence(evidenceId) + "/reviews");
        Assert.DoesNotContain("private/", await history.Content.ReadAsStringAsync());
        await using var verify = database.CreateContext();
        Assert.Contains("private/exact-v1", (await verify.Set<ProjectEvidenceReview>().FindAsync(review.Id))!.SourceSnapshotJson);
    }

    [Fact]
    public async Task Checkpoint_decision_audit_rollback_and_foreign_evidence_are_atomic()
    {
        using var app = new TrackingFactory(database); var x = await Prepare(); var foreign = await Prepare();
        using var primary = app.CreateAuthenticatedClient(x.Primary); using var foreignPrimary = app.CreateAuthenticatedClient(foreign.Primary);
        var foreignTarget = await Body<EvidenceReviewTarget>(await foreignPrimary.GetAsync(Evidence(foreign.Evidence) + "/review-target"));
        var review = await Body<EvidenceReviewDto>(await foreignPrimary.PostAsJsonAsync(Evidence(foreign.Evidence) + "/reviews", Review(foreignTarget)));
        var checkpoint = await Body<CheckpointDto>(await primary.PostAsJsonAsync(Project(x.S.ProjectId) + "/checkpoints", new SaveCheckpointRequest("Check", x.Major, new(Now))));
        var path = $"/api/v1/checkpoints/{checkpoint.Id}/decisions";
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.PostAsJsonAsync(path,
            new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "MET", "Foreign evidence", [new(foreign.Evidence, review.Id)]))).StatusCode);
        app.FailAudit = true;
        var input = new DecideCheckpointRequest(checkpoint.ConcurrencyToken, "AT_RISK", "Synthetic risk", []);
        Assert.Equal(HttpStatusCode.InternalServerError, (await primary.PostAsJsonAsync(path, input)).StatusCode);
        app.FailAudit = false;
        var history = await Body<PagedResult<CheckpointDto>>(await primary.GetAsync(Project(x.S.ProjectId) + "/checkpoints?pageSize=1"));
        Assert.Empty(history.Items.Single().Decisions); Assert.Equal(checkpoint.ConcurrencyToken, history.Items.Single().ConcurrencyToken);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => primary.PostAsJsonAsync(path, input)));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        await using var db = database.CreateContext();
        (await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == x.S.ProjectId)).SnapshotJson = "{}"; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync(Project(x.S.ProjectId) + "/checkpoints")).StatusCode);
    }

    [Fact]
    public async Task Committee_member_can_read_only_schedule_and_qualification_is_rechecked()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare();
        await AddSchedulingGates(a.S); await AddSchedulingGates(b.S);
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        await using (var db = database.CreateContext())
        {
            var profile = new M.SupervisorProfile { UserId = a.S.Scope.Users.NewLecturer, IsAvailable = true };
            db.Add(profile);
            profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(a.Major))!.Code });
            await db.SaveChangesAsync();
        }
        var committee = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees",
            new CreateCommitteeRequest("Independent lecturer committee", [new(a.S.Scope.Users.NewLecturer, a.Major)])));
        using var lecturer = app.CreateAuthenticatedClient(a.S.Scope.Users.NewLecturer);
        var input = new ScheduleDefenseRequest(committee.Id, new(Now.AddHours(3)), new(Now.AddHours(4)), "MEMBER-" + Guid.NewGuid().ToString("N"));
        var session = await Body<DefenseSessionDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", input));
        var read = await Body<DefenseSessionDto>(await lecturer.GetAsync($"/api/v1/defense-sessions/{session.Id}")); Assert.False(read.CanCancel);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(Project(a.S.ProjectId) + "/defense-sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lecturer.GetAsync(Evidence(a.Evidence) + "/review-target")).StatusCode);
        // Revoked qualification is checked again when scheduling a newly prepared committee.
        await using (var db = database.CreateContext()) { (await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.S.Scope.Users.NewLecturer)).IsAvailable = false; await db.SaveChangesAsync(); }
        await Body<DefenseSessionDto>(await staffA.PostAsJsonAsync($"/api/v1/defense-sessions/{session.Id}/cancel", new CancelDefenseRequest(session.ConcurrencyToken, "Replan")));
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", input)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staffB.GetAsync($"/api/v1/defense-sessions/{session.Id}")).StatusCode);
    }

    [Fact]
    public async Task Lecturer_conflict_cross_project_and_interdisciplinary_coverage_fail_closed()
    {
        using var app = new TrackingFactory(database); var a = await Prepare(); var b = await Prepare(); long additionalMajor;
        await AddSchedulingGates(a.S); await AddSchedulingGates(b.S);
        await using (var db = database.CreateContext())
        {
            // Prepare two synthetic projects in one academic organization for a shared lecturer.
            var semesterA = (await db.AcademicSemesters.FindAsync(a.S.Scope.SemesterId))!;
            (await db.AcademicSemesters.FindAsync(b.S.Scope.SemesterId))!.OrganizationId = semesterA.OrganizationId;
            (await db.Majors.FindAsync(b.Major))!.DepartmentId = a.S.Scope.Users.DepartmentId;
            (await db.Users.FindAsync(b.S.Scope.Users.Staff))!.DepartmentId = a.S.Scope.Users.DepartmentId;
            var snapshotB = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == b.S.ProjectId);
            var registrationB = JsonSerializer.Deserialize<AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence>(snapshotB.SnapshotJson)!;
            snapshotB.SnapshotJson = JsonSerializer.Serialize(registrationB with { OrganizationId = semesterA.OrganizationId,
                Scope = registrationB.Scope with { LeadDepartmentId = a.S.Scope.Users.DepartmentId }, DepartmentIds = [a.S.Scope.Users.DepartmentId],
                MajorDepartmentIds = new Dictionary<long, long> { [b.Major] = a.S.Scope.Users.DepartmentId } });
            var profile = await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.Primary); profile.IsAvailable = true;
            foreach (var majorId in new[] { a.Major, b.Major }) profile.SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(majorId))!.Code });
            var extra = new M.Major { Code = Guid.NewGuid().ToString("N"), Name = "Design", DepartmentId = a.S.Scope.Users.DepartmentId, IsActive = true };
            db.Add(extra); await db.SaveChangesAsync(); additionalMajor = extra.Id;
        }
        using var staffA = app.CreateAuthenticatedClient(a.S.Scope.Users.Staff); using var staffB = app.CreateAuthenticatedClient(b.S.Scope.Users.Staff);
        var committeeA = await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("A", [new(a.Primary, a.Major)])));
        var committeeB = await Body<CommitteeDto>(await staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("B", [new(a.Primary, b.Major)])));
        var inputA = new ScheduleDefenseRequest(committeeA.Id, new(Now.AddHours(5)), new(Now.AddHours(6)), "A-" + Guid.NewGuid().ToString("N"));
        var inputB = inputA with { CommitteeId = committeeB.Id, Room = "B-" + Guid.NewGuid().ToString("N") };
        var responses = await Task.WhenAll(staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-sessions", inputA), staffB.PostAsJsonAsync(Project(b.S.ProjectId) + "/defense-sessions", inputB));
        Assert.Single(responses.Where(r => r.IsSuccessStatusCode)); Assert.Single(responses.Where(r => r.StatusCode == HttpStatusCode.Conflict));
        var session = await Body<DefenseSessionDto>(responses.Single(r => r.IsSuccessStatusCode));
        var client = session.ProjectId == a.S.ProjectId ? staffA : staffB;
        var duplicate = session.ProjectId == a.S.ProjectId ? inputA : inputB;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Project(session.ProjectId) + "/defense-sessions", duplicate with { StartAt = new(Now.AddHours(7)), EndAt = new(Now.AddHours(8)) })).StatusCode);
        await using (var db = database.CreateContext())
        {
            var snapshot = await db.Set<ProjectRegistrationSnapshot>().SingleAsync(s => s.ProjectId == a.S.ProjectId);
            var registration = JsonSerializer.Deserialize<AIPMS.Application.Features.Projects.DTOs.RegistrationEvidence>(snapshot.SnapshotJson)!;
            snapshot.SnapshotJson = JsonSerializer.Serialize(registration with {
                Scope = registration.Scope with { ProjectMode = "INTERDISCIPLINARY", PrimaryMajorId = null,
                    Requirements = [.. registration.Scope.Requirements, new(additionalMajor, 0, 5, "Design evidence")] },
                MajorDepartmentIds = new Dictionary<long, long> { [a.Major] = a.S.Scope.Users.DepartmentId, [additionalMajor] = a.S.Scope.Users.DepartmentId } });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Missing Design", [new(a.Primary, a.Major)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("No Design expertise", [new(a.Primary, a.Major), new(a.Primary, additionalMajor)]))).StatusCode);
        await using (var db = database.CreateContext())
        { (await db.SupervisorProfiles.SingleAsync(p => p.UserId == a.Primary)).SupervisorExpertises.Add(new() { ExpertiseName = (await db.Majors.FindAsync(additionalMajor))!.Code }); await db.SaveChangesAsync(); }
        Assert.Equal(2, (await Body<CommitteeDto>(await staffA.PostAsJsonAsync(Project(a.S.ProjectId) + "/defense-committees", new CreateCommitteeRequest("Covered", [new(a.Primary, a.Major), new(a.Primary, additionalMajor)])))).Members.Count);
    }

    [Fact]
    public async Task Swagger_exposes_success_payloads_for_tracking_weekly_and_cold_routes()
    {
        using var app = new TrackingFactory(database); using var client = app.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach (var route in new[] { "/api/v1/project-evidence/{id}/review-target", "/api/v1/projects/{projectId}/checkpoints",
            "/api/v1/defense-sessions/{id}", "/api/v1/projects/{projectId}/weekly-assessments", "/api/v1/projects/{projectId}/cold/progress" })
            Assert.True(json.RootElement.GetProperty("paths").GetProperty(route).GetProperty("get").GetProperty("responses")
                .GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").TryGetProperty("$ref", out _), route);
        foreach (var schema in new[] { "EvidenceReviewTarget", "CheckpointDto", "DefenseSessionDto", "WeeklyAssessmentDto", "ColdProgressDto" })
            Assert.True(json.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty(schema, out _), schema);
    }

    private sealed class TrackingFactory(EvaluationDraftDatabaseFixture database) : EvaluationFactory(database)
    {
        public bool FailAudit { get; set; }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => {
                var existing = services.Single(d => d.ServiceType == typeof(IAuditTrail)); services.Remove(existing);
                services.AddScoped<IAuditTrail>(p => new TrackingAudit((IAuditTrail)ActivatorUtilities.CreateInstance(p, existing.ImplementationType!), this));
            });
        }
    }
    private sealed class TrackingAudit(IAuditTrail inner, TrackingFactory app) : IAuditTrail
    {
        public async Task RecordAsync(AuditEntry entry, CancellationToken ct = default)
        { await inner.RecordAsync(entry, ct); if (app.FailAudit) throw new InvalidOperationException("Synthetic academic audit failure"); }
    }
}
