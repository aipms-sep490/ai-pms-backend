using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Commands;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class MeetingCollaborationService(AipmsDbContext db, ICurrentUser user, IProjectAccessService access,
    ISender sender, IAuditTrail audit, TimeProvider clock) : IMeetingCollaborationService
{
    private long Actor => user.UserId ?? throw new UnauthorizedException();
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["input"] = [message] });
    private static void Current(Guid current, string supplied)
    {
        if (!Guid.TryParse(supplied, out var token) || token != current) throw new ConflictException("The resource changed. Reload before saving.");
    }
    private async Task<M.Meeting> Read(long id, CancellationToken ct)
    {
        var meeting = await db.Meetings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct)
            || !await access.CanAccessAsync(Actor, meeting.ProjectId, ct)) throw new ForbiddenException();
        return meeting;
    }
    private async Task<M.Project> LockProject(long id, CancellationToken ct)
    {
        var team = await db.Projects.Where(p => p.Id == id).Select(p => (long?)p.TeamId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Project", id);
        await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={team}").SingleAsync(ct);
        return await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").SingleAsync(ct);
    }
    private Task<bool> CanManage(M.Meeting meeting, CancellationToken ct) => MeetingGovernanceAuthority.CanManage(db, meeting, Actor, ct);
    public async Task<MeetingSeriesDto> CreateSeries(long projectId, CreateMeetingSeriesRequest input, CancellationToken ct)
    {
        if (input.Meeting is null || input.Recurrence is not ("NONE" or "WEEKLY" or "BIWEEKLY") || input.OccurrenceCount is < 1 or > 12
            || (input.Recurrence == "NONE" && input.OccurrenceCount != 1) || input.Meeting.ParticipantUserIds?.Count > 100)
            throw Invalid("Use NONE with one occurrence, or WEEKLY/BIWEEKLY with 1..12 occurrences and at most 100 participants.");
        if (input.Meeting.StartAt.Kind != DateTimeKind.Utc || input.Meeting.EndAt is { Kind: not DateTimeKind.Utc })
            throw Invalid("Series timestamps must be UTC (Z). Recurrence uses fixed 7/14-day UTC intervals.");
        var days = input.Recurrence == "BIWEEKLY" ? 14 : 7;
        var lastOffset = days * (input.OccurrenceCount - 1);
        if (input.Meeting.StartAt > DateTime.MaxValue.AddDays(-lastOffset) || input.Meeting.EndAt > DateTime.MaxValue.AddDays(-lastOffset))
            throw Invalid("Series dates exceed the supported range.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var project = await LockProject(projectId, ct);
        if (!await db.Users.AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct)) throw new ForbiddenException();
        if (project.Status != "ACTIVE") throw new ConflictException("The project is read-only.");
        var series = Guid.NewGuid(); var rows = new List<MeetingDto>();
        for (var i = 0; i < input.OccurrenceCount; i++)
        {
            var request = input.Meeting with { StartAt = input.Meeting.StartAt.AddDays(days * i), EndAt = input.Meeting.EndAt?.AddDays(days * i) };
            var dto = await sender.Send(new CreateMeetingCommand(projectId, request), ct);
            var row = await db.Meetings.SingleAsync(m => m.Id == dto.Id, ct);
            row.SeriesId = series; row.OccurrenceNumber = i + 1; row.Recurrence = input.Recurrence;
            await db.SaveChangesAsync(ct); rows.Add(dto);
        }
        await audit.RecordAsync(new(Actor, "MEETING_SERIES_CREATED", "PROJECT", projectId,
            new Dictionary<string, object?> { ["seriesId"] = series, ["recurrence"] = input.Recurrence, ["occurrenceCount"] = rows.Count }), ct);
        await tx.CommitAsync(ct);
        return new(series, input.Recurrence, rows);
    }
    public async Task<MeetingCollaborationDto> Get(long meetingId, CancellationToken ct)
    {
        var m = await Read(meetingId, ct); var manager = await CanManage(m, ct);
        var active = await db.Projects.AnyAsync(p => p.Id == m.ProjectId && p.Status == "ACTIVE", ct);
        var mine = await db.MeetingParticipants.AsNoTracking().SingleOrDefaultAsync(p => p.MeetingId == meetingId && p.UserId == Actor, ct);
        var deliveries = await db.Set<NotificationEmailDelivery>().AsNoTracking()
            .Where(d => d.NotificationRecipient.Notification.RelatedEntityType == "MEETING"
                && d.NotificationRecipient.Notification.RelatedEntityId == meetingId && (manager || d.NotificationRecipient.UserId == Actor))
            .OrderByDescending(d => d.NotificationRecipientId).Take(101)
            .Select(d => new MeetingEmailDeliveryDto(d.NotificationRecipient.UserId, d.NotificationRecipient.Notification.NotificationType,
                d.Status, d.AttemptCount, d.SentAt)).ToArrayAsync(ct);
        return new(m.Id, m.ConcurrencyToken.ToString("N"), m.SeriesId, m.OccurrenceNumber, m.Recurrence, m.NotesVersion,
            m.NotesLockedAt, m.NotesLockedBy, manager && active && m.Status == "COMPLETED" && !m.NotesLockedAt.HasValue,
            mine is null ? null : Rsvp(mine), deliveries.Take(100).ToArray(), deliveries.Length > 100);
    }
    public async Task<MeetingRsvpDto> Respond(long meetingId, RespondMeetingRequest input, CancellationToken ct)
    {
        if (input.Status is not ("ACCEPTED" or "TENTATIVE" or "DECLINED")) throw Invalid("RSVP must be ACCEPTED, TENTATIVE or DECLINED.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var initial = await Read(meetingId, ct); var project = await LockProject(initial.ProjectId, ct);
        var meeting = await db.Meetings.FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={meetingId}").SingleAsync(ct);
        await Read(meetingId, ct);
        if (project.Status != "ACTIVE" || meeting.Status != "SCHEDULED") throw new ConflictException("RSVP is closed.");
        var participant = await db.MeetingParticipants.SingleOrDefaultAsync(p => p.MeetingId == meetingId && p.UserId == Actor, ct) ?? throw new ForbiddenException();
        Current(participant.RsvpToken, input.ConcurrencyToken);
        participant.RsvpStatus = input.Status; participant.RsvpToken = Guid.NewGuid(); participant.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "MEETING_RSVP_UPDATED", "MEETING", meetingId,
            new Dictionary<string, object?> { ["status"] = input.Status }), ct);
        await tx.CommitAsync(ct); return Rsvp(participant);
    }
    public async Task<MeetingCollaborationDto> LockNotes(long meetingId, LockMeetingNotesRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var initial = await Read(meetingId, ct); var project = await LockProject(initial.ProjectId, ct);
        var m = await db.Meetings.FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={meetingId}").SingleAsync(ct);
        await Read(meetingId, ct);
        if (!await CanManage(m, ct)) throw new ForbiddenException();
        Current(m.ConcurrencyToken, input.ConcurrencyToken);
        if (project.Status != "ACTIVE" || m.Status != "COMPLETED" || m.NotesLockedAt.HasValue)
            throw new ConflictException("Only completed, unlocked meeting notes in an active project can be locked.");
        m.NotesLockedAt = clock.GetUtcNow().UtcDateTime; m.NotesLockedBy = Actor; m.UpdatedAt = m.NotesLockedAt.Value; m.ConcurrencyToken = Guid.NewGuid();
        await MeetingNotesHistory.Capture(db, m, Actor, m.UpdatedAt, ct);
        await audit.RecordAsync(new(Actor, "MEETING_NOTES_LOCKED", "MEETING", meetingId,
            new Dictionary<string, object?> { ["version"] = m.NotesVersion }), ct);
        await tx.CommitAsync(ct); return await Get(meetingId, ct);
    }
    public async Task<PagedResult<MeetingNotesVersionDto>> NotesHistory(long meetingId, int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw Invalid("Invalid pagination.");
        await Read(meetingId, ct);
        var q = db.Set<MeetingNotesVersion>().AsNoTracking().Where(v => v.MeetingId == meetingId);
        var count = await q.LongCountAsync(ct);
        var rows = await q.OrderByDescending(v => v.Version).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(v => new MeetingNotesVersionDto(v.Id, v.MeetingId, v.Version, v.IsLocked, v.MeetingNotes,
            v.Minutes, v.Decisions, v.Blockers, v.ChangedBy, v.ChangedAt,
            JsonSerializer.Deserialize<MeetingGovernanceSnapshotDto>(v.GovernanceJson) ?? new([], [], []))).ToArray(), page, pageSize, count);
    }
    private static MeetingRsvpDto Rsvp(M.MeetingParticipant p) => new(p.MeetingId, p.UserId, p.RsvpStatus, p.RsvpToken.ToString("N"));
}
