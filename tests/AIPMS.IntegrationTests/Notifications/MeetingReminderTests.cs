using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.Infrastructure.Services.Projects;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Delivery = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class MeetingReminderTests(FinalSubmissionDraftDatabaseFixture fixture)
    : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static readonly DateTime Now = FinalSubmissionDraftDatabaseFixture.Now;
    private static MeetingReminderService Service(AipmsDbContext db) => new(db, new ProjectAccessService(db));

    private async Task<(FinalDraftScenario Scope, long Meeting)> Seed(int minutes = 15)
    {
        // Only this class uses this owned fixture database. Finish prior test deliveries so
        // queue assertions do not depend on xUnit's test order.
        await using var db = fixture.CreateContext();
        await db.Set<Delivery>().ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, "FAILED"));
        var scope = await fixture.Seed();
        var meeting = new M.Meeting { ProjectId = scope.ProjectId, Title = "Design review", CreatedBy = scope.Users.Student,
            Status = "SCHEDULED", StartAt = Now.AddMinutes(minutes), EndAt = Now.AddHours(1), Location = "Room 1",
            MeetingParticipants = [new() { UserId = scope.Users.Student, AttendanceStatus = "ACCEPTED" }] };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        return (scope, meeting.Id);
    }

    private async Task Enqueue(long id, DateTime? now = null)
    {
        await using var db = fixture.CreateContext();
        await Service(db).EnqueueAsync(id, now ?? Now, 15, default);
    }

    [Theory]
    [InlineData(16, false)]
    [InlineData(15, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public async Task Only_future_meetings_within_fifteen_minutes_are_due(int minutes, bool expected)
    {
        var (_, id) = await Seed(minutes);
        await using var db = fixture.CreateContext();
        var ids = await Service(db).GetDueMeetingIdsAsync(id - 1, 1, Now, 15, default);
        Assert.Equal(expected, ids.Contains(id));
        await Enqueue(id);
        Assert.Equal(expected, await db.Notifications.AnyAsync(n => n.RelatedEntityType == "MEETING" && n.RelatedEntityId == id));
    }

    [Fact]
    public async Task Concurrent_and_repeated_sweeps_enqueue_once_and_new_invitee_receives_once()
    {
        var (s, id) = await Seed();
        await Task.WhenAll(Enqueue(id), Enqueue(id));
        await Enqueue(id);
        await using var db = fixture.CreateContext();
        var notification = await db.Notifications.SingleAsync(n => n.RelatedEntityType == "MEETING" && n.RelatedEntityId == id);
        Assert.Single(await db.NotificationRecipients.Where(r => r.NotificationId == notification.Id).ToListAsync());
        db.MeetingParticipants.Add(new() { MeetingId = id, UserId = s.MemberId, AttendanceStatus = "INVITED" });
        await db.SaveChangesAsync();
        await Enqueue(id);
        await Enqueue(id);
        Assert.Equal(2, await db.NotificationRecipients.CountAsync(r => r.NotificationId == notification.Id));
        Assert.Equal(2, await db.Set<Delivery>().CountAsync(d => d.NotificationRecipient.NotificationId == notification.Id));
    }

    [Theory]
    [InlineData("DECLINED")]
    [InlineData("INACTIVE")]
    [InlineData("REMOVED_MEMBER")]
    [InlineData("UNASSIGNED_LECTURER")]
    public async Task Ineligible_invitees_do_not_receive_content(string reason)
    {
        var (s, id) = await Seed();
        await using var db = fixture.CreateContext();
        var participant = await db.MeetingParticipants.SingleAsync(p => p.MeetingId == id);
        if (reason == "DECLINED") participant.AttendanceStatus = "DECLINED";
        if (reason == "INACTIVE") (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE";
        if (reason == "REMOVED_MEMBER") (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Users.Student)).LeftAt = Now;
        if (reason == "UNASSIGNED_LECTURER") participant.UserId = s.Users.Lecturer;
        await db.SaveChangesAsync();
        await Enqueue(id);
        Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityType == "MEETING" && n.RelatedEntityId == id));
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("COMPLETED")]
    [InlineData("PROJECT_ENDED")]
    [InlineData("RESCHEDULED")]
    [InlineData("STARTED")]
    [InlineData("UNINVITED")]
    [InlineData("REMOVED_MEMBER")]
    [InlineData("INACTIVE")]
    public async Task Queue_rechecks_state_and_scope_before_SMTP(string change)
    {
        var (s, id) = await Seed();
        await Enqueue(id);
        await using var db = fixture.CreateContext();
        var meeting = await db.Meetings.FindAsync(id);
        if (change is "CANCELLED" or "COMPLETED") meeting!.Status = change;
        if (change == "RESCHEDULED") meeting!.StartAt = Now.AddMinutes(40);
        if (change == "PROJECT_ENDED") (await db.Projects.FindAsync(s.ProjectId))!.Status = "COMPLETED";
        if (change == "UNINVITED") db.MeetingParticipants.Remove(await db.MeetingParticipants.SingleAsync(p => p.MeetingId == id));
        if (change == "REMOVED_MEMBER") (await db.TeamMembers.SingleAsync(m => m.TeamId == s.TeamId && m.UserId == s.Users.Student)).LeftAt = Now;
        if (change == "INACTIVE") (await db.Users.FindAsync(s.Users.Student))!.Status = "INACTIVE";
        await db.SaveChangesAsync();
        var delivery = await new NotificationEmailQueue(db).ClaimNextAsync(change == "STARTED" ? Now.AddMinutes(15) : Now, default,
            IMeetingReminderService.NotificationType);
        Assert.NotNull(delivery);
        Assert.False(delivery.ShouldSend);
        var stored = await db.Set<Delivery>().AsNoTracking().SingleAsync(d => d.NotificationRecipientId == delivery.RecipientId);
        Assert.Equal("FAILED", stored.Status);
        Assert.Equal("MEETING_REMINDER_OBSOLETE", stored.LastError);
        Assert.Null(stored.SentAt);
    }

    [Fact]
    public async Task Reschedule_creates_new_occurrence_and_renders_current_details_in_Vietnam_time()
    {
        var (_, id) = await Seed();
        await Enqueue(id);
        await using var db = fixture.CreateContext();
        var meeting = await db.Meetings.FindAsync(id);
        meeting!.StartAt = Now.AddMinutes(10);
        meeting.Title = "Updated title";
        meeting.Location = "Room 2";
        await db.SaveChangesAsync();
        await Enqueue(id);
        var queue = new NotificationEmailQueue(db);
        Assert.False((await queue.ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType))!.ShouldSend);
        var current = (await queue.ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType))!;
        Assert.True(current.ShouldSend);
        Assert.Contains("Updated title", current.Subject);
        Assert.Contains("Room 2", current.Body);
        Assert.Contains("12/09/2026 17:10 (Vietnam, UTC+7)", current.Body);
        await queue.MarkSentAsync(current.RecipientId, Now, default, current.AttemptCount);
        await Enqueue(id);
        Assert.Null(await queue.ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType));
    }

    [Fact]
    public async Task SMTP_retry_is_durable_and_stale_worker_cannot_overwrite_reclaimed_attempt()
    {
        var (_, id) = await Seed();
        await Enqueue(id);
        await using var db1 = fixture.CreateContext();
        var q1 = new NotificationEmailQueue(db1);
        var first = (await q1.ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType))!;
        await using var db2 = fixture.CreateContext();
        var q2 = new NotificationEmailQueue(db2);
        Assert.Null(await q2.ClaimNextAsync(Now.AddMinutes(5), default, IMeetingReminderService.NotificationType));
        var retry = (await q2.ClaimNextAsync(Now.AddMinutes(7), default, IMeetingReminderService.NotificationType))!;
        Assert.Equal(2, retry.AttemptCount);
        await q1.MarkSentAsync(first.RecipientId, Now.AddMinutes(7), default, first.AttemptCount);
        await q1.MarkFailedAsync(first.RecipientId, Now, "old worker", true, default, first.AttemptCount);
        Assert.Equal("SENDING", await db1.Set<Delivery>().AsNoTracking().Where(d => d.NotificationRecipientId == first.RecipientId).Select(d => d.Status).SingleAsync());
        await q2.MarkFailedAsync(retry.RecipientId, Now.AddMinutes(8), "SMTP timeout", false, default, retry.AttemptCount);
        await using var db3 = fixture.CreateContext();
        var q3 = new NotificationEmailQueue(db3);
        Assert.Null(await q3.ClaimNextAsync(Now.AddMinutes(7), default, IMeetingReminderService.NotificationType));
        var third = (await q3.ClaimNextAsync(Now.AddMinutes(8), default, IMeetingReminderService.NotificationType))!;
        Assert.Equal(3, third.AttemptCount);
        await q3.MarkSentAsync(third.RecipientId, Now.AddMinutes(8), default, third.AttemptCount);
        Assert.Equal("SENT", await db3.Set<Delivery>().AsNoTracking().Where(d => d.NotificationRecipientId == third.RecipientId).Select(d => d.Status).SingleAsync());
    }

    [Fact]
    public async Task Reminder_worker_cannot_claim_unrelated_email_backlog()
    {
        var (s, id) = await Seed();
        await using var db = fixture.CreateContext();
        var notification = new M.Notification { NotificationType = "TASK_OVERDUE", Title = "Old backlog", Content = "Old backlog",
            NotificationRecipients = [new() { UserId = s.Users.Student }] };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        db.Set<Delivery>().Add(new() { NotificationRecipientId = notification.NotificationRecipients.Single().Id, NextAttemptAt = Now.AddDays(-1) });
        await db.SaveChangesAsync();
        await Enqueue(id);
        var claimed = await new NotificationEmailQueue(db).ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType);
        Assert.NotNull(claimed);
        Assert.Contains("Design review", claimed.Subject);
        Assert.Equal("PENDING", await db.Set<Delivery>().AsNoTracking().Where(d => d.NotificationRecipient.NotificationId == notification.Id).Select(d => d.Status).SingleAsync());
    }

    [Theory]
    [InlineData("PRIMARY")]
    [InlineData("DISCIPLINE_MENTOR")]
    public async Task Invited_supervisor_receives_reminder_but_ended_assignment_is_suppressed(string type)
    {
        var (s, id) = await Seed();
        await using var db = fixture.CreateContext();
        (await db.MeetingParticipants.SingleAsync(p => p.MeetingId == id)).UserId = s.Users.Lecturer;
        var major = await db.ProjectMajors.Where(m => m.ProjectId == s.ProjectId).Select(m => m.MajorId).SingleAsync();
        var request = new M.SupervisorRequest { ProjectId = s.ProjectId, SupervisorProfileId = s.Users.ProfileId,
            RequestedBy = s.Users.Student, Status = "ACCEPTED", RequestedAt = Now.AddDays(-1),
            AssignmentType = type, MajorId = type == "PRIMARY" ? null : major };
        var assignment = new M.SupervisorAssignment { ProjectId = s.ProjectId, SupervisorRequest = request,
            SupervisorProfileId = s.Users.ProfileId, IsPrimary = type == "PRIMARY", AssignmentType = type,
            MajorId = request.MajorId, AssignedAt = Now.AddDays(-1) };
        db.SupervisorAssignments.Add(assignment);
        await db.SaveChangesAsync();
        await Enqueue(id);
        var queue = new NotificationEmailQueue(db);
        var first = (await queue.ClaimNextAsync(Now, default, IMeetingReminderService.NotificationType))!;
        Assert.True(first.ShouldSend);
        await queue.MarkFailedAsync(first.RecipientId, Now.AddMinutes(1), "SMTP unavailable", false, default, first.AttemptCount);
        assignment.EndedAt = Now;
        await db.SaveChangesAsync();
        var retry = (await queue.ClaimNextAsync(Now.AddMinutes(1), default, IMeetingReminderService.NotificationType))!;
        Assert.False(retry.ShouldSend);
    }

    [Fact]
    public async Task Failure_during_email_insert_rolls_back_inbox_and_occurrence()
    {
        var (_, id) = await Seed();
        var options = new DbContextOptionsBuilder<AipmsDbContext>().UseSqlServer(fixture.ConnectionString)
            .AddInterceptors(new RejectEmailInsert()).Options;
        await using (var failing = new AipmsDbContext(options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(failing).EnqueueAsync(id, Now, 15, default));
        await using var db = fixture.CreateContext();
        Assert.False(await db.Notifications.AnyAsync(n => n.RelatedEntityType == "MEETING" && n.RelatedEntityId == id));
        await Enqueue(id);
        Assert.Single(await db.Notifications.Where(n => n.RelatedEntityType == "MEETING" && n.RelatedEntityId == id).ToListAsync());
    }

    private sealed class RejectEmailInsert : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Delivery>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("Injected queue failure");
            return ValueTask.FromResult(result);
        }
    }
}
