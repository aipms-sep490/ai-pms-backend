using System.Net;
using System.Net.Http.Json;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.ActionItems.DTOs;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AIPMS.IntegrationTests.Evaluations;

public sealed class MeetingCollaborationEndpointTests(EvaluationDraftDatabaseFixture database) : IClassFixture<EvaluationDraftDatabaseFixture>
{
    private static DateTime Now => EvaluationDraftDatabaseFixture.Now;
    private static async Task<T> Body<T>(HttpResponseMessage response)
    { Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<T>())!; }
    private static string Meeting(long id) => $"/api/v1/meetings/{id}";
    private async Task<(EvaluationScenario S, long Primary)> Seed()
    {
        var s = await database.Seed(false); await using var db = database.CreateContext();
        (await db.Projects.FindAsync(s.ProjectId))!.Status = "ACTIVE";
        var primary = await db.SupervisorAssignments.Where(a => a.ProjectId == s.ProjectId).Select(a => a.SupervisorProfile.UserId).SingleAsync();
        await db.SaveChangesAsync(); return (s, primary);
    }
    private static CreateMeetingRequest Request(long primary) => new("Synthetic meeting", null, Now.AddMinutes(10), Now.AddMinutes(40), "Demo room", null, [primary], MeetingDeliveryMode: "ONSITE", VideoChannel: "NONE");

    [Fact]
    public async Task Series_creates_independent_occurrences_notifications_and_deduplicated_reminders()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var series = await Body<MeetingSeriesDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meeting-series", new CreateMeetingSeriesRequest(Request(x.Primary), "WEEKLY", 3)));
        Assert.Equal(3, series.Occurrences.Count);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Now.AddMinutes(10).AddDays(7 * i), series.Occurrences[i].StartAt);
            var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(series.Occurrences[i].Id) + "/collaboration"));
            Assert.Equal(series.SeriesId, state.SeriesId); Assert.Equal(i + 1, state.OccurrenceNumber);
            Assert.Contains(state.EmailDeliveries, d => d.UserId == x.Primary && d.NotificationType == "MEETING_SCHEDULED" && d.Status == "PENDING" && d.SentAt == null);
            using var scope = app.Services.CreateScope(); var reminder = scope.ServiceProvider.GetRequiredService<IMeetingReminderService>();
            await reminder.EnqueueAsync(state.MeetingId, Now.AddDays(i * 7), 15, default);
            await reminder.EnqueueAsync(state.MeetingId, Now.AddDays(i * 7), 15, default);
        }
        var first = series.Occurrences[0];
        await Body<MeetingDto>(await leader.PostAsync(Meeting(first.Id) + "/cancel?concurrencyToken=" + first.ConcurrencyToken, null));
        await using var db = database.CreateContext();
        var ids = series.Occurrences.Select(m => m.Id).ToArray();
        Assert.Equal(3, await db.Set<ScheduledNotificationOccurrence>().CountAsync(o => o.ProjectId == x.S.ProjectId));
        Assert.Equal(2, await db.Meetings.CountAsync(m => ids.Contains(m.Id) && m.Status == "SCHEDULED"));
        Assert.Equal(3, await db.AuditLogs.CountAsync(a => a.Action == "MEETING_SCHEDULED" && ids.Select(id => id.ToString()).Contains(a.EntityId!)));
    }

    [Fact]
    public async Task Rsvp_is_self_only_concurrent_safe_and_never_updates_official_attendance()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        using var primary = app.CreateAuthenticatedClient(x.Primary); using var outsider = app.CreateAuthenticatedClient(x.S.Scope.Users.OutsideStaff);
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        var state = await Body<MeetingCollaborationDto>(await primary.GetAsync(Meeting(m.Id) + "/collaboration"));
        var input = new RespondMeetingRequest("TENTATIVE", state.MyRsvp!.ConcurrencyToken);
        var replies = await Task.WhenAll(primary.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", input), primary.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", input));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        var rsvp = await Body<MeetingRsvpDto>(replies.Single(r => r.IsSuccessStatusCode));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", input)).StatusCode);
        await Body<MeetingRsvpDto>(await primary.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", new RespondMeetingRequest("DECLINED", rsvp.ConcurrencyToken)));
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<IMeetingReminderService>().EnqueueAsync(m.Id, Now, 15, default);
        await using var db = database.CreateContext();
        var participant = await db.MeetingParticipants.SingleAsync(p => p.MeetingId == m.Id && p.UserId == x.Primary);
        Assert.Equal("INVITED", participant.AttendanceStatus); Assert.Equal("DECLINED", participant.RsvpStatus);
        Assert.False(await db.NotificationRecipients.AnyAsync(r => r.UserId == x.Primary && r.Notification.RelatedEntityId == m.Id && r.Notification.NotificationType == "MEETING_START_REMINDER"));
    }

    [Fact]
    public async Task Locked_notes_and_action_definitions_are_immutable_but_progress_and_attendance_remain_separate()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        m = await Body<MeetingDto>(await leader.PutAsJsonAsync(Meeting(m.Id) + "/notes", new UpdateMeetingNotesRequest("Original notes", null, m.ConcurrencyToken)));
        m = await Body<MeetingDto>(await leader.PostAsync(Meeting(m.Id) + "/complete?concurrencyToken=" + m.ConcurrencyToken, null));
        var action = await Body<MeetingActionItemDto>(await leader.PostAsJsonAsync(Meeting(m.Id) + "/action-items", new SaveMeetingActionItemRequest("Follow up", null, x.S.Scope.Users.Student, null, "OPEN", m.ConcurrencyToken!)));
        var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration")); Assert.True(state.CanLockNotes);
        var stale = state.ConcurrencyToken;
        state = await Body<MeetingCollaborationDto>(await leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(stale)));
        Assert.False(state.CanLockNotes); Assert.Equal(3, state.NotesVersion);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(stale))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Meeting(m.Id) + "/notes", new UpdateMeetingNotesRequest("Overwrite", null, state.ConcurrencyToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Meeting(m.Id) + "/decisions", new CreateMeetingDecisionRequest("Late decision", state.ConcurrencyToken))).StatusCode);
        var update = new SaveMeetingActionItemRequest("Follow up", null, x.S.Scope.Users.Student, null, "DONE", action.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Meeting(m.Id) + $"/action-items/{action.Id}", update with { Title = "Changed" })).StatusCode);
        await Body<MeetingActionItemDto>(await leader.PutAsJsonAsync(Meeting(m.Id) + $"/action-items/{action.Id}", update));
        state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        await Body<MeetingDto>(await leader.PutAsJsonAsync(Meeting(m.Id) + "/notes", new UpdateMeetingNotesRequest(null, [new(x.Primary, "ATTENDED")], state.ConcurrencyToken)));
        var history = await Body<PagedResult<MeetingNotesVersionDto>>(await leader.GetAsync(Meeting(m.Id) + "/notes/history"));
        Assert.Equal(3, history.TotalCount); Assert.True(history.Items.First().IsLocked); Assert.All(history.Items, v => Assert.Equal("Original notes", v.MeetingNotes));
        Assert.Equal("Follow up", history.Items.First().Governance.Actions.Single().Title);
        await using var db = database.CreateContext();
        Assert.Equal("INVITED", (await db.MeetingParticipants.SingleAsync(p => p.MeetingId == m.Id && p.UserId == x.Primary)).RsvpStatus);
        Assert.Equal("COMPLETED", (await db.Meetings.FindAsync(m.Id))!.Status);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == "MEETING_NOTES_LOCKED" && a.EntityId == m.Id.ToString()));
    }

    [Fact]
    public async Task Invalid_series_and_foreign_participant_create_no_partial_meetings()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var url = $"/api/v1/projects/{x.S.ProjectId}/meeting-series";
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(url, new CreateMeetingSeriesRequest(Request(x.Primary), "WEEKLY", 13))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await leader.PostAsJsonAsync(url, new CreateMeetingSeriesRequest(Request(x.Primary), "NONE", 2))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(url, new CreateMeetingSeriesRequest(Request(x.S.Scope.Users.OutsideStaff), "BIWEEKLY", 2))).StatusCode);
        await using var db = database.CreateContext(); Assert.False(await db.Meetings.AnyAsync(m => m.ProjectId == x.S.ProjectId));
    }

    [Fact]
    public async Task Project_action_item_routes_cannot_bypass_notes_lock()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        var url = $"/api/v1/projects/{x.S.ProjectId}/action-items";
        var request = new CreateProjectActionItemRequest("MEETING", "Existing action", m.Id, OwnerId: x.S.Scope.Users.Student);
        var action = await Body<ProjectActionItemDto>(await leader.PostAsJsonAsync(url, request));
        var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        m = await Body<MeetingDto>(await leader.PostAsync(Meeting(m.Id) + "/complete?concurrencyToken=" + state.ConcurrencyToken, null));
        await Body<MeetingCollaborationDto>(await leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(m.ConcurrencyToken!)));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(url, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(url + $"/{action.Id}", new UpdateProjectActionItemRequest("Changed", ConcurrencyToken: action.ConcurrencyToken))).StatusCode);
        action = await Body<ProjectActionItemDto>(await leader.PostAsJsonAsync(url + $"/{action.Id}/status", new UpdateProjectActionItemStatusRequest("IN_PROGRESS", action.ConcurrencyToken)));
        await Body<ProjectActionItemDto>(await leader.PostAsJsonAsync(url + $"/{action.Id}/status", new UpdateProjectActionItemStatusRequest("DONE", action.ConcurrencyToken)));
        var history = await Body<PagedResult<MeetingNotesVersionDto>>(await leader.GetAsync(Meeting(m.Id) + "/notes/history"));
        Assert.Equal("Existing action", history.Items.First().Governance.ProjectActions.Single().Title);
    }

    [Fact]
    public async Task Audit_failure_rolls_back_series_rsvp_and_notes_lock()
    {
        using var app = new EvaluationFactory(database); using var failing = new EvaluationFactory(database, failAudit: true);
        var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        using var broken = failing.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var request = new CreateMeetingSeriesRequest(Request(x.Primary), "BIWEEKLY", 3);
        Assert.Equal(HttpStatusCode.InternalServerError, (await broken.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meeting-series", request)).StatusCode);
        await using (var db = database.CreateContext()) Assert.False(await db.Meetings.AnyAsync(m => m.ProjectId == x.S.ProjectId));
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        Assert.Equal(HttpStatusCode.InternalServerError, (await broken.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", new RespondMeetingRequest("TENTATIVE", state.MyRsvp!.ConcurrencyToken))).StatusCode);
        var after = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration")); Assert.Equal(state.MyRsvp, after.MyRsvp);
        m = await Body<MeetingDto>(await leader.PostAsync(Meeting(m.Id) + "/complete?concurrencyToken=" + m.ConcurrencyToken, null));
        Assert.Equal(HttpStatusCode.InternalServerError, (await broken.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(m.ConcurrencyToken!))).StatusCode);
        after = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        Assert.Null(after.NotesLockedAt); Assert.Equal(0, after.NotesVersion);
    }

    [Fact]
    public async Task Scope_and_terminal_state_are_rechecked_and_swagger_exposes_success_contracts()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        using var primary = app.CreateAuthenticatedClient(x.Primary); using var outsider = app.CreateAuthenticatedClient(x.S.Scope.Users.OutsideStaff);
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync(Meeting(m.Id) + "/notes/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(m.ConcurrencyToken!))).StatusCode);
        await using (var db = database.CreateContext())
        { (await db.SupervisorAssignments.SingleAsync(a => a.ProjectId == x.S.ProjectId))!.EndedAt = Now; await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Forbidden, (await primary.GetAsync(Meeting(m.Id) + "/collaboration")).StatusCode);
        var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        await Body<MeetingDto>(await leader.PostAsync(Meeting(m.Id) + "/cancel?concurrencyToken=" + m.ConcurrencyToken, null));
        Assert.Equal(HttpStatusCode.Conflict, (await leader.PutAsJsonAsync(Meeting(m.Id) + "/rsvp", new RespondMeetingRequest("TENTATIVE", state.MyRsvp!.ConcurrencyToken))).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Meeting(m.Id) + "/collaboration")).StatusCode);
        using var swagger = System.Text.Json.JsonDocument.Parse(await anonymous.GetStringAsync("/swagger/v1/swagger.json"));
        Assert.True(swagger.RootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("MeetingGovernanceSnapshotDto", out _));
        Assert.True(swagger.RootElement.GetProperty("paths").GetProperty("/api/v1/meetings/{meetingId}/collaboration").GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
    }

    [Fact]
    public async Task Concurrent_notes_save_and_lock_have_one_winner_and_a_consistent_final_snapshot()
    {
        using var app = new EvaluationFactory(database); var x = await Seed(); using var leader = app.CreateAuthenticatedClient(x.S.Scope.Users.Student);
        var m = await Body<MeetingDto>(await leader.PostAsJsonAsync($"/api/v1/projects/{x.S.ProjectId}/meetings", Request(x.Primary)));
        m = await Body<MeetingDto>(await leader.PostAsync(Meeting(m.Id) + "/complete?concurrencyToken=" + m.ConcurrencyToken, null));
        var responses = await Task.WhenAll(leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(m.ConcurrencyToken!)),
            leader.PutAsJsonAsync(Meeting(m.Id) + "/notes", new UpdateMeetingNotesRequest("Concurrent content", null, m.ConcurrencyToken)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var state = await Body<MeetingCollaborationDto>(await leader.GetAsync(Meeting(m.Id) + "/collaboration"));
        if (state.CanLockNotes) await Body<MeetingCollaborationDto>(await leader.PostAsJsonAsync(Meeting(m.Id) + "/notes/lock", new LockMeetingNotesRequest(state.ConcurrencyToken)));
        await using var db = database.CreateContext();
        var current = (await db.Meetings.FindAsync(m.Id))!;
        var locked = await db.Set<MeetingNotesVersion>().SingleAsync(v => v.MeetingId == m.Id && v.IsLocked);
        Assert.Equal(current.MeetingNotes, locked.MeetingNotes); Assert.Equal(current.NotesVersion, locked.Version);
    }
}
