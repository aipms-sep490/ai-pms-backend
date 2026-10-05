using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Meetings.Abstractions;
using AIPMS.Application.Features.Meetings.Video;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using AIPMS.Infrastructure.Video;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class MeetingVideoService(AipmsDbContext db, ICurrentUser current, IProjectAccessService access,
    IVideoMeetingProvider provider, IOptions<VideoMeetingOptions> options, TimeProvider clock, IAuditTrail audit,
    IVideoCleanupScheduler cleanup) : IMeetingVideoService
{
    private readonly VideoMeetingOptions settings = options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<VideoSessionDto> GetSessionAsync(long meetingId, CancellationToken ct)
    {
        var (meeting, actor) = await LoadAsync(meetingId, ct);
        var session = await Latest(meetingId, ct);
        var manager = await CanManageAsync(meeting, actor, ct);
        var participant = await IsParticipant(meetingId, actor, ct);
        var reasons = new List<string>();
        var enabled = settings.Enabled && settings.IsReady && meeting.VideoChannel == "IN_APP_VIDEO";
        if (!enabled) reasons.Add("VIDEO_NOT_ENABLED");
        if (meeting.Project.Status != "ACTIVE") reasons.Add("PROJECT_NOT_ACTIVE");
        if (meeting.Status != "SCHEDULED") reasons.Add("MEETING_NOT_SCHEDULED");
        if (session is null || session is { Status: "CREATED", StartedAt: null }) reasons.Add("VIDEO_SESSION_NOT_STARTED");
        if (session?.Status == "ENDED") reasons.Add("VIDEO_SESSION_ENDED");
        if (session?.Status == "FAILED") reasons.Add("VIDEO_SESSION_FAILED");
        if (!participant) reasons.Add("MEETING_PARTICIPANT_REQUIRED");
        if (!manager) { reasons.Add("VIDEO_START_FORBIDDEN"); reasons.Add("VIDEO_END_FORBIDDEN"); }
        var window = Window(meeting);
        if (Now < window.AvailableFrom) reasons.Add("VIDEO_JOIN_TOO_EARLY");
        if (window.AvailableUntil is DateTime end && Now > end) reasons.Add("VIDEO_JOIN_WINDOW_CLOSED");
        var valid = enabled && meeting.Project.Status == "ACTIVE" && meeting.Status == "SCHEDULED";
        var active = session is { Status: "CREATED" or "LIVE" };
        var caps = new VideoCapabilities(valid && manager && !active,
            valid && participant && active && session!.StartedAt != null && InWindow(meeting),
            valid && manager && participant && active, false);
        // Metadata reads must not require an online media provider. Count remains unknown until queried reliably.
        return new(meetingId, meeting.Status, meeting.MeetingDeliveryMode, meeting.VideoChannel,
            session is null ? null : new(session.Id, session.Status, session.Provider, session.StartedAt, session.EndedAt, null, session.ConcurrencyToken.ToString("N")),
            caps, meeting.VideoChannel == "IN_APP_VIDEO" ? window : null, reasons);
    }

    public async Task<VideoSessionDto> StartAsync(long meetingId, CancellationToken ct)
    {
        MeetingVideoSession session;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var (meeting, actor) = await LockAndLoad(meetingId, ct);
            EnsureEnabled(); EnsureStartable(meeting);
            if (!await CanManageAsync(meeting, actor, ct)) throw Denied("VIDEO_START_FORBIDDEN");
            var existing = await Latest(meetingId, ct);
            if (existing is { Status: "CREATED" or "LIVE" })
            {
                await tx.CommitAsync(ct);
                return await GetSessionAsync(meetingId, ct);
            }
            if (!await IsParticipant(meetingId, actor, ct))
            {
                db.MeetingParticipants.Add(new() { MeetingId = meetingId, UserId = actor, AttendanceStatus = "INVITED", CreatedAt = Now, UpdatedAt = Now });
                meeting.ConcurrencyToken = Guid.NewGuid();
            }
            session = new() { MeetingId = meetingId, Provider = provider.Provider, ProviderRoomKey = "vm-" + Guid.NewGuid().ToString("N"),
                Status = "CREATED", StartedBy = actor, CreatedAt = Now, UpdatedAt = Now };
            db.MeetingVideoSessions.Add(session);
            await db.SaveChangesAsync(ct);
            await Record(actor, "VIDEO_SESSION_CREATED", session.Id, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        try
        {
            await provider.CreateRoomAsync(new(session.ProviderRoomKey, meetingId), ct);
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var (meeting, actor) = await LockAndLoad(meetingId, ct);
            var row = await db.MeetingVideoSessions.SingleAsync(x => x.Id == session.Id, ct);
            if (row.Status is "CREATED" or "LIVE" && meeting.Status == "SCHEDULED" && meeting.Project.Status == "ACTIVE")
            {
                row.StartedAt ??= Now;
                row.UpdatedAt = Now;
                row.ConcurrencyToken = Guid.NewGuid();
            }
            else await cleanup.EnqueueForMeetingAsync(meetingId, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is ServiceUnavailableException or VideoProviderPermanentException or OperationCanceledException or HttpRequestException)
        {
            db.ChangeTracker.Clear();
            var code = ex is ServiceUnavailableException { Code: not null } unavailable ? unavailable.Code : "VIDEO_PROVIDER_UNAVAILABLE";
            await db.MeetingVideoSessions.Where(x => x.Id == session.Id && x.Status == "CREATED")
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.Status, "FAILED").SetProperty(p => p.FailureCode, code)
                    .SetProperty(p => p.UpdatedAt, Now).SetProperty(p => p.ConcurrencyToken, Guid.NewGuid()), CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new ServiceUnavailableException("Video room creation failed. Try again later.", code);
        }
        return await GetSessionAsync(meetingId, ct);
    }

    public async Task<VideoJoinCredentialDto> JoinAsync(long meetingId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var (meeting, actor) = await LockAndLoad(meetingId, ct);
        EnsureEnabled(); EnsureStartable(meeting);
        if (!await IsParticipant(meetingId, actor, ct)) throw Denied("MEETING_PARTICIPANT_REQUIRED");
        var session = await Latest(meetingId, ct);
        if (session is null) throw State("VIDEO_SESSION_NOT_STARTED");
        if (session.Status == "ENDED") throw State("VIDEO_SESSION_ENDED");
        if (session.Status == "FAILED") throw State("VIDEO_SESSION_FAILED");
        if (session.StartedAt is null) throw State("VIDEO_SESSION_NOT_STARTED");
        if (!InWindow(meeting)) throw State(Now < Window(meeting).AvailableFrom ? "VIDEO_JOIN_TOO_EARLY" : "VIDEO_JOIN_WINDOW_CLOSED");
        var binding = await db.MeetingVideoParticipantBindings.SingleOrDefaultAsync(x => x.MeetingVideoSessionId == session.Id && x.UserId == actor, ct);
        if (binding is null)
        {
            binding = new() { MeetingVideoSessionId = session.Id, UserId = actor, ProviderParticipantIdentity = "vp-" + Guid.NewGuid().ToString("N"), CreatedAt = Now };
            db.MeetingVideoParticipantBindings.Add(binding);
        }
        var moderator = await CanManageAsync(meeting, actor, ct);
        var name = await db.Users.Where(x => x.Id == actor).Select(x => x.FullName).SingleAsync(ct);
        // Signing is local: the provider abstraction must not perform network I/O when issuing credentials.
        var credential = await provider.CreateJoinCredentialAsync(new(session.ProviderRoomKey, binding.ProviderParticipantIdentity, name, moderator, Now.AddSeconds(settings.JoinTokenTtlSeconds)), ct);
        await Record(actor, "VIDEO_JOIN_CREDENTIAL_ISSUED", session.Id, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return credential;
    }

    public async Task<VideoSessionDto> EndAsync(long meetingId, CancellationToken ct)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var (meeting, actor) = await LockAndLoad(meetingId, ct);
            EnsureEnabled(); EnsureStartable(meeting);
            if (!await CanManageAsync(meeting, actor, ct) || !await IsParticipant(meetingId, actor, ct)) throw Denied("VIDEO_END_FORBIDDEN");
            var session = await Latest(meetingId, ct) ?? throw State("VIDEO_SESSION_NOT_STARTED");
            if (session.Status is "ENDED" or "FAILED") throw State(session.Status == "ENDED" ? "VIDEO_SESSION_ENDED" : "VIDEO_SESSION_FAILED");
            session.Status = "ENDED";
            session.EndedAt = Now;
            session.UpdatedAt = Now;
            session.ConcurrencyToken = Guid.NewGuid();
            await cleanup.EnqueueForMeetingAsync(meetingId, ct);
            await Record(actor, "VIDEO_SESSION_ENDED", session.Id, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        return await GetSessionAsync(meetingId, ct);
    }

    public async Task<VideoPresenceDto> GetPresenceAsync(long meetingId, CancellationToken ct)
    {
        await LoadAsync(meetingId, ct);
        var now = Now;
        var raw = await db.MeetingVideoPresenceSessions.AsNoTracking().Where(x => x.MeetingVideoSession.MeetingId == meetingId)
            .Select(x => new { x.UserId, x.JoinedAt, x.LeftAt,
                Boundary = x.MeetingVideoSession.EndedAt ?? (x.MeetingVideoSession.Status == "ENDED" || x.MeetingVideoSession.Status == "FAILED"
                    ? (DateTime?)x.MeetingVideoSession.UpdatedAt : null) }).ToListAsync(ct);
        // Exclude impossible legacy evidence and never let an ended room accrue more time.
        var rows = raw.Where(x => (x.LeftAt == null || x.LeftAt >= x.JoinedAt) && (x.Boundary == null || x.JoinedAt <= x.Boundary))
            .Select(x => new { x.UserId, x.JoinedAt, LeftAt = x.Boundary is { } end && (x.LeftAt == null || x.LeftAt > end) ? end : x.LeftAt }).ToList();
        return new(meetingId, rows.GroupBy(x => x.UserId).Select(g => new VideoPresenceParticipantDto(g.Key, g.Min(x => (DateTime?)x.JoinedAt),
            g.Any(x => x.LeftAt == null) ? null : g.Max(x => x.LeftAt),
            g.Sum(x => Math.Max(0, (long)((x.LeftAt ?? now) - x.JoinedAt).TotalSeconds)), g.Count())).ToList());
    }

    private async Task<(Meeting, long)> LockAndLoad(long id, CancellationToken ct)
    {
        var scope = await db.Meetings.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ProjectId, x.Project.TeamId }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Meeting", id);
        // Same lock order as execution mutations: team, project, meeting, then account.
        await db.Teams.FromSqlInterpolated($"SELECT * FROM dbo.teams WITH (UPDLOCK,HOLDLOCK) WHERE id={scope.TeamId}").AsNoTracking().SingleAsync(ct);
        await db.Projects.FromSqlInterpolated($"SELECT * FROM dbo.projects WITH (UPDLOCK,HOLDLOCK) WHERE id={scope.ProjectId}").AsNoTracking().SingleAsync(ct);
        await db.Meetings.FromSqlInterpolated($"SELECT * FROM dbo.meetings WITH (UPDLOCK,HOLDLOCK) WHERE id={id}").AsNoTracking().SingleAsync(ct);
        var actor = current.UserId ?? throw new UnauthorizedException();
        await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK,HOLDLOCK) WHERE id={actor}").AsNoTracking().SingleOrDefaultAsync(ct);
        return await LoadAsync(id, ct);
    }
    private async Task<(Meeting, long)> LoadAsync(long id, CancellationToken ct)
    {
        var actor = current.UserId ?? throw new UnauthorizedException();
        var meeting = await db.Meetings.Include(x => x.Project).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Meeting", id);
        if (!await db.Users.AnyAsync(x => x.Id == actor && x.Status == "ACTIVE", ct) || !await access.CanAccessAsync(actor, meeting.ProjectId, ct)) throw Denied("VIDEO_JOIN_FORBIDDEN");
        return (meeting, actor);
    }
    private Task<MeetingVideoSession?> Latest(long id, CancellationToken ct) => db.MeetingVideoSessions.Where(x => x.MeetingId == id).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    private Task<bool> IsParticipant(long id, long actor, CancellationToken ct) => db.MeetingParticipants.AnyAsync(x => x.MeetingId == id && x.UserId == actor, ct);
    private async Task<bool> CanManageAsync(Meeting m, long actor, CancellationToken ct) => m.CreatedBy == actor
        || await db.TeamMembers.AnyAsync(x => x.TeamId == m.Project.TeamId && x.UserId == actor && x.IsLeader && x.LeftAt == null && x.User.UserRoleUsers.Any(r => r.Role.Code == "STUDENT"), ct)
        || await db.SupervisorAssignments.AnyAsync(x => x.ProjectId == m.ProjectId && x.EndedAt == null && x.IsPrimary && x.SupervisorProfile.UserId == actor, ct);
    private void EnsureEnabled() { if (!settings.Enabled || !settings.IsReady) throw new ServiceUnavailableException("Video is disabled.", "VIDEO_NOT_ENABLED"); }
    private static void EnsureStartable(Meeting m)
    {
        if (m.Project.Status != "ACTIVE") throw State("PROJECT_NOT_ACTIVE");
        if (m.Status != "SCHEDULED") throw State("MEETING_NOT_SCHEDULED");
        if (m.VideoChannel != "IN_APP_VIDEO") throw State("VIDEO_NOT_ENABLED");
    }
    private bool InWindow(Meeting m) => Now >= Window(m).AvailableFrom && (Window(m).AvailableUntil is null || Now <= Window(m).AvailableUntil);
    private VideoJoinWindow Window(Meeting m) => new(m.StartAt.AddMinutes(-settings.JoinOpenBeforeMinutes), m.EndAt?.AddMinutes(settings.JoinCloseAfterMinutes));
    private static ConflictException State(string code) => new("The video operation is not available in the current state.", code);
    private static ForbiddenException Denied(string code) => new("You do not have permission for this video operation.", code);
    private Task Record(long actor, string action, long sessionId, CancellationToken ct) => audit.RecordAsync(new(actor, action, "MEETING_VIDEO_SESSION", sessionId, new Dictionary<string, object?>()), ct);
}


