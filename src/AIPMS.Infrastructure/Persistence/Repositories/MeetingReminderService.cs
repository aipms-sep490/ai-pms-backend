using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;
using Delivery = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class MeetingReminderService(AipmsDbContext db, IProjectAccessService access) : IMeetingReminderService
{
    internal const string SuppressedCode = "MEETING_REMINDER_OBSOLETE";
    internal static string Key(long meetingId, DateTime start) => FormattableString.Invariant($"MEETING_START_REMINDER:{meetingId}:{start.Ticks}");

    public async Task<IReadOnlyList<long>> GetDueMeetingIdsAsync(long afterId, int limit, DateTime nowUtc, int minutesBefore, CancellationToken ct)
    {
        Validate(nowUtc, minutesBefore);
        var horizon = nowUtc.AddMinutes(minutesBefore);
        return await db.Meetings.AsNoTracking().Where(m => m.Id > afterId && m.Status == "SCHEDULED"
                && m.Project.Status == "ACTIVE" && m.StartAt > nowUtc && m.StartAt <= horizon)
            .OrderBy(m => m.Id).Select(m => m.Id).Take(Math.Clamp(limit, 1, 500)).ToListAsync(ct);
    }

    public async Task EnqueueAsync(long meetingId, DateTime nowUtc, int minutesBefore, CancellationToken ct)
    {
        Validate(nowUtc, minutesBefore);
        var scope = await db.Meetings.AsNoTracking().Where(m => m.Id == meetingId)
            .Select(m => new { m.ProjectId, m.Project.TeamId }).SingleOrDefaultAsync(ct);
        if (scope is null) return;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Follow the execution mutation lock order and commit occurrence, inbox and email together.
        await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={scope.TeamId}").AsNoTracking().SingleAsync(ct);
        var project = await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={scope.ProjectId}").AsNoTracking().SingleAsync(ct);
        var meeting = await db.Meetings.FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={meetingId}").AsNoTracking().SingleOrDefaultAsync(ct);
        if (meeting is null || project.Status != "ACTIVE" || meeting.Status != "SCHEDULED"
            || meeting.StartAt <= nowUtc || meeting.StartAt > nowUtc.AddMinutes(minutesBefore)) return;
        var candidates = await EligibleParticipants(db, meetingId).Select(p => p.UserId).ToListAsync(ct);
        var recipients = new List<long>();
        foreach (var id in candidates)
            if (await access.CanAccessAsync(id, project.Id, ct)) recipients.Add(id);
        if (recipients.Count == 0) return;

        var key = Key(meeting.Id, meeting.StartAt);
        var occurrence = await db.Set<ScheduledNotificationOccurrence>().Include(x => x.Notification).ThenInclude(n => n.NotificationRecipients)
            .SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.OccurrenceKey == key, ct);
        if (occurrence is null)
        {
            occurrence = new() { ProjectId = project.Id, OccurrenceKey = key, Notification = new()
            {
                NotificationType = IMeetingReminderService.NotificationType, Title = Subject(meeting), Content = Body(meeting, project.Code),
                RelatedEntityType = "MEETING", RelatedEntityId = meeting.Id, CreatedAt = nowUtc, UpdatedAt = nowUtc
            } };
            db.Set<ScheduledNotificationOccurrence>().Add(occurrence);
        }
        foreach (var id in recipients.Where(id => occurrence.Notification.NotificationRecipients.All(r => r.UserId != id)))
            occurrence.Notification.NotificationRecipients.Add(new() { UserId = id, CreatedAt = nowUtc, UpdatedAt = nowUtc, DeliveredAt = nowUtc });
        await db.SaveChangesAsync(ct);
        var ids = occurrence.Notification.NotificationRecipients.Where(r => recipients.Contains(r.UserId)).Select(r => r.Id).ToArray();
        var queued = await db.Set<Delivery>().Where(d => ids.Contains(d.NotificationRecipientId)).Select(d => d.NotificationRecipientId).ToListAsync(ct);
        db.Set<Delivery>().AddRange(ids.Except(queued).Select(id => new Delivery { NotificationRecipientId = id, NextAttemptAt = nowUtc }));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal static IQueryable<M.MeetingParticipant> EligibleParticipants(AipmsDbContext db, long meetingId) =>
        db.MeetingParticipants.AsNoTracking().Where(p => p.MeetingId == meetingId && p.User.Status == "ACTIVE"
            && (p.AttendanceStatus == null || p.AttendanceStatus == "INVITED" || p.AttendanceStatus == "ACCEPTED"));

    internal static async Task<(string Subject, string Body)?> CurrentEmailAsync(AipmsDbContext db, IProjectAccessService access,
        long notificationId, long userId, DateTime now, CancellationToken ct)
    {
        var occurrence = await db.Set<ScheduledNotificationOccurrence>().AsNoTracking().Where(o => o.NotificationId == notificationId)
            .Select(o => new { o.ProjectId, o.OccurrenceKey, MeetingId = o.Notification.RelatedEntityId }).SingleOrDefaultAsync(ct);
        if (occurrence?.MeetingId is not long meetingId) return null;
        var meeting = await db.Meetings.AsNoTracking().Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting is null || meeting.ProjectId != occurrence.ProjectId || meeting.Status != "SCHEDULED" || meeting.Project.Status != "ACTIVE"
            || meeting.StartAt <= now || occurrence.OccurrenceKey != Key(meeting.Id, meeting.StartAt)
            || !await EligibleParticipants(db, meeting.Id).AnyAsync(p => p.UserId == userId, ct)
            || !await access.CanAccessAsync(userId, meeting.ProjectId, ct)) return null;
        return (Subject(meeting), Body(meeting, meeting.Project.Code));
    }

    private static string Subject(M.Meeting meeting)
    {
        var title = "Meeting starts soon: " + meeting.Title.Replace('\r', ' ').Replace('\n', ' ');
        return title.Length <= 255 ? title : title[..255];
    }

    private static string Body(M.Meeting meeting, string projectCode)
    {
        var start = DateTime.SpecifyKind(meeting.StartAt, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        var location = string.IsNullOrWhiteSpace(meeting.Location) ? "Online" : meeting.Location;
        var channel = meeting.VideoChannel == "IN_APP_VIDEO" ? "Open AI-PMS > Meetings > this meeting to join the video room."
            : meeting.VideoChannel == "EXTERNAL_LINK" ? "Meeting link: " + meeting.OnlineUrl : "";
        return $"Your meeting is starting soon.\n\nMeeting: {meeting.Title}\nProject: {projectCode}\nStart: {start} (Vietnam, UTC+7)\nLocation: {location}\n{channel}";
    }

    private static void Validate(DateTime now, int minutes)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(now));
        if (minutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(minutes));
    }
}
