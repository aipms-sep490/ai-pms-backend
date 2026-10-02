using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class MeetingVideoService(AipmsDbContext db, ICurrentUser current, IProjectAccessService access, IVideoMeetingProvider provider, IOptions<VideoMeetingOptions> options, TimeProvider clock) : IMeetingVideoService
{
    private readonly VideoMeetingOptions settings = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<VideoSessionDto> GetSessionAsync(long meetingId, CancellationToken cancellationToken)
    {
        var (meeting, actor) = await LoadAsync(meetingId, cancellationToken);
        var session = await db.MeetingVideoSessions.AsNoTracking().Where(x => x.MeetingId == meetingId).OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        var canManage = await CanManageAsync(meeting, actor, cancellationToken);
        var participant = await db.MeetingParticipants.AnyAsync(x => x.MeetingId == meetingId && x.UserId == actor && x.User.Status == "ACTIVE", cancellationToken);
        var canJoin = participant && meeting.Project.Status == "ACTIVE" && meeting.Status == "SCHEDULED" && meeting.VideoChannel == "IN_APP_VIDEO" && session is { Status: "CREATED" or "LIVE" } && InWindow(meeting);
        var reasons = new List<string>();
        if (!settings.Enabled) reasons.Add("VIDEO_NOT_ENABLED");
        if (meeting.Project.Status != "ACTIVE") reasons.Add("PROJECT_NOT_ACTIVE");
        if (meeting.Status != "SCHEDULED") reasons.Add("MEETING_NOT_SCHEDULED");
        if (meeting.VideoChannel != "IN_APP_VIDEO") reasons.Add("VIDEO_NOT_ENABLED");
        if (session is null) reasons.Add("VIDEO_SESSION_NOT_STARTED");
        else if (session.Status == "ENDED") reasons.Add("VIDEO_SESSION_ENDED");
        else if (session.Status == "FAILED") reasons.Add("VIDEO_SESSION_FAILED");
        if (!participant) reasons.Add("MEETING_PARTICIPANT_REQUIRED");
        var canStart = settings.Enabled && settings.IsReady && canManage && meeting.Status == "SCHEDULED" && meeting.Project.Status == "ACTIVE" && meeting.VideoChannel == "IN_APP_VIDEO" && (session is null || session.Status is "ENDED" or "FAILED");
        var canEnd = settings.Enabled && settings.IsReady && canManage && meeting.Status == "SCHEDULED" && meeting.Project.Status == "ACTIVE" && session is { Status: "CREATED" or "LIVE" };
        return new VideoSessionDto(meetingId, meeting.Status, meeting.MeetingDeliveryMode, meeting.VideoChannel, session is null ? null : await ToDto(session, cancellationToken), new VideoCapabilities(canStart, canJoin, canEnd, false), meeting.VideoChannel == "IN_APP_VIDEO" ? Window(meeting) : null, reasons.Distinct().ToArray());
    }

    public async Task<VideoSessionDto> StartAsync(long meetingId, CancellationToken cancellationToken)
    {
        var (meeting, actor) = await LoadAsync(meetingId, cancellationToken);
        EnsureEnabled(); EnsureStartable(meeting); if (!await CanManageAsync(meeting, actor, cancellationToken)) throw new ForbiddenException("VIDEO_START_FORBIDDEN");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var active = await db.MeetingVideoSessions.FromSqlInterpolated($"SELECT * FROM dbo.meeting_video_sessions WITH (UPDLOCK, ROWLOCK) WHERE meeting_id = {meetingId} AND status IN ('CREATED','LIVE')").SingleOrDefaultAsync(cancellationToken);
        if (active is not null) { await tx.CommitAsync(cancellationToken); return await GetSessionAsync(meetingId, cancellationToken); }
        if (!await db.MeetingParticipants.AnyAsync(x => x.MeetingId == meetingId && x.UserId == actor, cancellationToken))
        {
            db.MeetingParticipants.Add(new MeetingParticipant
            {
                MeetingId = meetingId,
                UserId = actor,
                AttendanceStatus = "ACCEPTED",
                CreatedAt = Now,
                UpdatedAt = Now
            });
        }
        var session = new MeetingVideoSession { MeetingId = meetingId, Provider = provider.Provider, ProviderRoomKey = "vm-" + Guid.NewGuid().ToString("N"), Status = "CREATED", StartedBy = actor, CreatedAt = Now, UpdatedAt = Now };
        db.MeetingVideoSessions.Add(session); await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        try
        {
            await provider.CreateRoomAsync(new VideoProviderRoomRequest(session.ProviderRoomKey, meetingId), cancellationToken);
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or TimeoutException or HttpRequestException)
        {
            // The academic meeting transaction is already committed; persist a failed video session
            // so a retry can safely create a new room without duplicating meeting side effects.
            session.Status = "FAILED";
            session.FailureCode = ex is TimeoutException ? "VIDEO_PROVIDER_TIMEOUT" : ex is ServiceUnavailableException { Code: not null } unavailable ? unavailable.Code : "VIDEO_PROVIDER_UNAVAILABLE";
            session.UpdatedAt = Now;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        return await GetSessionAsync(meetingId, cancellationToken);
    }

    public async Task<VideoJoinCredentialDto> JoinAsync(long meetingId, CancellationToken cancellationToken)
    {
        var (meeting, actor) = await LoadAsync(meetingId, cancellationToken); EnsureEnabled(); EnsureStartable(meeting);
        if (!InWindow(meeting)) throw new ConflictException("The meeting is outside its join window.", meeting.StartAt > Now ? "VIDEO_JOIN_TOO_EARLY" : "VIDEO_JOIN_WINDOW_CLOSED");
        var session = await db.MeetingVideoSessions.FirstOrDefaultAsync(x => x.MeetingId == meetingId && (x.Status == "CREATED" || x.Status == "LIVE"), cancellationToken) ?? throw new ConflictException("The video room has not been started.", "VIDEO_SESSION_NOT_STARTED");
        var participant = await db.MeetingParticipants.Include(x => x.User).FirstOrDefaultAsync(x => x.MeetingId == meetingId && x.UserId == actor && x.User.Status == "ACTIVE", cancellationToken) ?? throw new ForbiddenException("MEETING_PARTICIPANT_REQUIRED");
        var moderator = await CanManageAsync(meeting, actor, cancellationToken);
        var binding = await db.MeetingVideoParticipantBindings.FirstOrDefaultAsync(x => x.MeetingVideoSessionId == session.Id && x.UserId == actor, cancellationToken);
        var identity = binding?.ProviderParticipantIdentity ?? "vp-" + Guid.NewGuid().ToString("N"); var expires = Now.AddSeconds(settings.JoinTokenTtlSeconds);
        var credential = await provider.CreateJoinCredentialAsync(new VideoProviderJoinRequest(session.ProviderRoomKey, identity, participant.User.FullName, moderator, expires), cancellationToken);
        if (binding is null)
        {
            db.MeetingVideoParticipantBindings.Add(new MeetingVideoParticipantBinding { MeetingVideoSessionId = session.Id, UserId = actor, ProviderParticipantIdentity = identity, CreatedAt = Now });
            await db.SaveChangesAsync(cancellationToken);
        }
        return credential;
    }

    public async Task<VideoSessionDto> EndAsync(long meetingId, CancellationToken cancellationToken)
    {
        var (meeting, actor) = await LoadAsync(meetingId, cancellationToken); EnsureEnabled(); EnsureStartable(meeting); if (!await CanManageAsync(meeting, actor, cancellationToken)) throw new ForbiddenException("VIDEO_END_FORBIDDEN");
        var session = await db.MeetingVideoSessions.FirstOrDefaultAsync(x => x.MeetingId == meetingId && (x.Status == "CREATED" || x.Status == "LIVE"), cancellationToken) ?? throw new ConflictException("The video room is not active.", "VIDEO_SESSION_NOT_STARTED");
        session.Status = "ENDED"; session.EndedAt = Now; session.UpdatedAt = Now; await db.SaveChangesAsync(cancellationToken); await provider.CloseRoomAsync(session.ProviderRoomKey, cancellationToken); return await GetSessionAsync(meetingId, cancellationToken);
    }

    public async Task<VideoPresenceDto> GetPresenceAsync(long meetingId, CancellationToken cancellationToken)
    {
        var (meeting, actor) = await LoadAsync(meetingId, cancellationToken); if (!await access.CanAccessAsync(actor, meeting.ProjectId, cancellationToken)) throw new ForbiddenException();
        var rows = await db.MeetingVideoPresenceSessions.AsNoTracking().Where(x => x.MeetingVideoSession.MeetingId == meetingId).ToListAsync(cancellationToken);
        var result = rows.GroupBy(x => x.UserId).Select(g => new VideoPresenceParticipantDto(g.Key, g.Min(x => (DateTime?)x.JoinedAt), g.Max(x => x.LeftAt), g.Sum(x => (long)((x.LeftAt ?? Now) - x.JoinedAt).TotalSeconds), g.Count())).ToList();
        return new VideoPresenceDto(meetingId, result);
    }

    private async Task<(Meeting Meeting, long Actor)> LoadAsync(long meetingId, CancellationToken ct)
    {
        var actor = current.UserId ?? throw new UnauthorizedException();
        var meeting = await db.Meetings.Include(x => x.Project).FirstOrDefaultAsync(x => x.Id == meetingId, ct) ?? throw new NotFoundException("Meeting", meetingId);
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.Status == "ACTIVE", ct)) throw new ForbiddenException("The account is not active.");
        if (!await access.CanAccessAsync(actor, meeting.ProjectId, ct)) throw new ForbiddenException();
        return (meeting, actor);
    }
    private async Task<bool> CanManageAsync(Meeting meeting, long actor, CancellationToken ct) => meeting.CreatedBy == actor || (await db.TeamMembers.AnyAsync(x => x.TeamId == meeting.Project.TeamId && x.UserId == actor && x.IsLeader && x.LeftAt == null, ct)) || (await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == meeting.ProjectId && x.EndedAt == null && x.IsPrimary && x.SupervisorProfile.UserId == actor, ct));
    private void EnsureEnabled() { if (!settings.Enabled || !settings.IsReady) throw new ServiceUnavailableException("VIDEO_NOT_ENABLED"); }
    private static void EnsureStartable(Meeting m) { if (m.Project.Status != "ACTIVE") throw new ConflictException("PROJECT_NOT_ACTIVE", "PROJECT_NOT_ACTIVE"); if (m.Status != "SCHEDULED") throw new ConflictException("MEETING_NOT_SCHEDULED", "MEETING_NOT_SCHEDULED"); if (m.VideoChannel != "IN_APP_VIDEO") throw new ConflictException("VIDEO_NOT_ENABLED", "VIDEO_NOT_ENABLED"); }
    private bool InWindow(Meeting m) => Now >= m.StartAt.AddMinutes(-settings.JoinOpenBeforeMinutes) && (!m.EndAt.HasValue || Now <= m.EndAt.Value.AddMinutes(settings.JoinCloseAfterMinutes));
    private VideoJoinWindow Window(Meeting m) => new(m.StartAt.AddMinutes(-settings.JoinOpenBeforeMinutes), m.EndAt?.AddMinutes(settings.JoinCloseAfterMinutes));
    private async Task<VideoSessionStateDto> ToDto(MeetingVideoSession s, CancellationToken ct)
    {
        VideoProviderRoomSnapshot? room = null;
        if (s.Status is "CREATED" or "LIVE")
        {
            try { room = await provider.GetRoomAsync(s.ProviderRoomKey, ct); }
            catch (ServiceUnavailableException) { /* The persisted session remains readable during provider outages. */ }
        }
        return new VideoSessionStateDto(s.Id, s.Status, s.Provider, s.StartedAt, s.EndedAt, room?.ActiveParticipantCount, s.ConcurrencyToken.ToString("N"));
    }
}


