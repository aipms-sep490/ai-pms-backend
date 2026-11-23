using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
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
                NotificationType = IMeetingReminderService.NotificationType, Title = Subject(meeting), Content = InAppBody(meeting, project.Code, project.Title),
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
            && p.RsvpStatus != "DECLINED"
            && (p.AttendanceStatus == null || p.AttendanceStatus == "INVITED" || p.AttendanceStatus == "ACCEPTED"));

    internal sealed record MeetingEmailContent(string Subject, string Body, string ActionUrl, string ActionLabel);

    internal static async Task<MeetingEmailContent?> CurrentEmailAsync(
        AipmsDbContext db,
        IProjectAccessService access,
        long notificationId,
        long userId,
        DateTime now,
        CancellationToken ct,
        string? frontendBaseUrl = null)
    {
        var occurrence = await db.Set<ScheduledNotificationOccurrence>().AsNoTracking().Where(o => o.NotificationId == notificationId)
            .Select(o => new { o.ProjectId, o.OccurrenceKey, MeetingId = o.Notification.RelatedEntityId }).SingleOrDefaultAsync(ct);
        if (occurrence?.MeetingId is not long meetingId) return null;
        var meeting = await db.Meetings.AsNoTracking().Include(m => m.Project).SingleOrDefaultAsync(m => m.Id == meetingId, ct);
        if (meeting is null || meeting.ProjectId != occurrence.ProjectId || meeting.Status != "SCHEDULED" || meeting.Project.Status != "ACTIVE"
            || meeting.StartAt <= now || occurrence.OccurrenceKey != Key(meeting.Id, meeting.StartAt)
            || !await EligibleParticipants(db, meeting.Id).AnyAsync(p => p.UserId == userId, ct)
            || !await access.CanAccessAsync(userId, meeting.ProjectId, ct)) return null;

        var links = await BuildMeetingLinksAsync(db, meeting, userId, frontendBaseUrl, ct);
        return new MeetingEmailContent(
            Subject(meeting),
            EmailBody(meeting, meeting.Project.Code, meeting.Project.Title, links),
            links.ActionUrl,
            links.ActionLabel);
    }

    internal sealed record MeetingLinks(string ActionUrl, string ActionLabel, string DetailUrl, string? ExternalUrl);

    internal static async Task<MeetingLinks> BuildMeetingLinksAsync(
        AipmsDbContext db,
        M.Meeting meeting,
        long userId,
        string? frontendBaseUrl,
        CancellationToken ct)
    {
        var baseUrl = NormalizeBaseUrl(frontendBaseUrl);

        var isTeamMember = await db.TeamMembers.AsNoTracking()
            .AnyAsync(tm => tm.TeamId == meeting.Project.TeamId && tm.UserId == userId && tm.LeftAt == null, ct);

        string routePrefix;
        if (isTeamMember)
        {
            routePrefix = $"/project/meetings/{meeting.Id}";
        }
        else
        {
            var supervisorAssignment = await db.SupervisorAssignments.AsNoTracking()
                .Where(a => a.ProjectId == meeting.ProjectId && a.EndedAt == null && a.SupervisorProfile.UserId == userId)
                .OrderByDescending(a => a.IsPrimary)
                .Select(a => new { a.IsPrimary, a.AssignmentType, a.MajorId })
                .FirstOrDefaultAsync(ct);

            if (supervisorAssignment is not null && !supervisorAssignment.IsPrimary
                && supervisorAssignment.AssignmentType == "DISCIPLINE_MENTOR"
                && supervisorAssignment.MajorId is long majorId)
            {
                routePrefix = $"/mentor/projects/{meeting.ProjectId}/majors/{majorId}/meetings/{meeting.Id}";
            }
            else if (supervisorAssignment is not null)
            {
                routePrefix = $"/supervisor/projects/{meeting.ProjectId}/meetings/{meeting.Id}";
            }
            else
            {
                var isLecturer = await db.UserRoles.AsNoTracking()
                    .AnyAsync(ur => ur.UserId == userId && ur.Role.Code == AppRoles.Lecturer, ct);
                routePrefix = isLecturer
                    ? $"/supervisor/projects/{meeting.ProjectId}/meetings/{meeting.Id}"
                    : $"/project/meetings/{meeting.Id}";
            }
        }

        var detailUrl = $"{baseUrl}{routePrefix}";
        var videoUrl = $"{detailUrl}/video";
        var externalUrl = IsSafeHttpUrl(meeting.OnlineUrl) ? meeting.OnlineUrl!.Trim() : null;

        if (meeting.VideoChannel == "EXTERNAL_LINK" && externalUrl is not null)
            return new MeetingLinks(externalUrl, "Tham gia cuộc họp trực tuyến", detailUrl, externalUrl);

        if (meeting.VideoChannel == "IN_APP_VIDEO")
            return new MeetingLinks(videoUrl, "Tham gia phòng họp video ngay", detailUrl, null);

        return new MeetingLinks(detailUrl, "Xem chi tiết cuộc họp", detailUrl, externalUrl);
    }

    internal static string FormatVnTime(DateTime utcTime) =>
        DateTime.SpecifyKind(utcTime, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    internal static string FormatDeliveryLabel(M.Meeting meeting)
    {
        if (meeting.VideoChannel == "IN_APP_VIDEO") return "Họp trực tuyến (Phòng video AI-PMS)";
        if (meeting.VideoChannel == "EXTERNAL_LINK") return "Họp trực tuyến (Liên kết ngoài)";
        return (meeting.MeetingDeliveryMode ?? "").ToUpperInvariant() switch
        {
            "REMOTE" => "Trực tuyến",
            "HYBRID" => "Kết hợp trực tiếp & trực tuyến",
            "ONSITE" => "Trực tiếp",
            _ => string.IsNullOrWhiteSpace(meeting.Location) ? "Trực tuyến" : "Trực tiếp"
        };
    }

    internal static string FormatLocation(M.Meeting meeting)
    {
        if (!string.IsNullOrWhiteSpace(meeting.Location)) return meeting.Location.Trim();
        if (meeting.VideoChannel is "IN_APP_VIDEO" or "EXTERNAL_LINK") return "Phòng họp trực tuyến";
        return "Trực tuyến";
    }

    internal static bool IsSafeHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string NormalizeBaseUrl(string? baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(baseUrl)
            && Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }
        return "http://localhost:5173";
    }

    private static string Subject(M.Meeting meeting)
    {
        var title = "Sắp đến giờ họp: " + meeting.Title.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return title.Length <= 255 ? title : title[..255];
    }

    private static string InAppBody(M.Meeting meeting, string projectCode, string? projectTitle)
    {
        var start = FormatVnTime(meeting.StartAt);
        var delivery = FormatDeliveryLabel(meeting);
        var location = FormatLocation(meeting);
        var projectLabel = string.IsNullOrWhiteSpace(projectTitle) ? projectCode : $"{projectCode} – {projectTitle}";

        var lines = new List<string>
        {
            $"Cuộc họp \"{meeting.Title}\" sắp bắt đầu lúc {start} (Việt Nam, UTC+7).",
            $"• Đồ án: {projectLabel}",
            $"• Hình thức: {delivery}",
            $"• Địa điểm: {location}"
        };

        if (meeting.VideoChannel == "EXTERNAL_LINK" && IsSafeHttpUrl(meeting.OnlineUrl))
            lines.Add($"• Liên kết tham gia: {meeting.OnlineUrl!.Trim()}");
        else if (meeting.VideoChannel == "IN_APP_VIDEO")
            lines.Add("• Tham gia: Nhấn \"Xem cuộc họp\" bên dưới để vào phòng họp video AI-PMS.");

        return string.Join('\n', lines);
    }

    private static string EmailBody(M.Meeting meeting, string projectCode, string? projectTitle, MeetingLinks links)
    {
        var start = FormatVnTime(meeting.StartAt);
        var delivery = FormatDeliveryLabel(meeting);
        var location = FormatLocation(meeting);
        var projectLabel = string.IsNullOrWhiteSpace(projectTitle) ? projectCode : $"{projectCode} – {projectTitle}";

        var lines = new List<string>
        {
            $"Cuộc họp \"{meeting.Title}\" của nhóm sắp diễn ra. Vui lòng chuẩn bị nội dung và tham gia đúng giờ.",
            string.Empty,
            $"• Cuộc họp: {meeting.Title}",
            $"• Đồ án: {projectLabel}",
            $"• Thời gian bắt đầu: {start} (Việt Nam, UTC+7)",
            $"• Hình thức: {delivery}",
            $"• Địa điểm: {location}",
            $"• {links.ActionLabel}: {links.ActionUrl}"
        };

        if (!string.Equals(links.ActionUrl, links.DetailUrl, StringComparison.OrdinalIgnoreCase))
            lines.Add($"• Chi tiết cuộc họp & biên bản trên AI-PMS: {links.DetailUrl}");

        return string.Join('\n', lines);
    }

    private static void Validate(DateTime now, int minutes)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(now));
        if (minutes is < 1 or > 1440) throw new ArgumentOutOfRangeException(nameof(minutes));
    }
}
