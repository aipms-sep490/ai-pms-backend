using System.Text.Json;
using System.Threading.Tasks;
using System.Globalization;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.ReviewGates;
using AIPMS.Application.Features.ReviewGates.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class GateSessionScriptService(AipmsDbContext db, ICurrentUser current,
    IAuditTrail audit, IWorkingDayCalculator workingDays, TimeProvider clock) : IGateSessionScriptService
{
    private long Actor => current.UserId ?? throw new UnauthorizedException();
    private DateTimeOffset Now => clock.GetUtcNow();
    private static ValidationException Invalid(string field, string reason) =>
        new(new Dictionary<string, string[]> { [field] = [reason] });
    private static T[] Parse<T>(string json) => JsonSerializer.Deserialize<T[]>(json) ?? [];
    private static void Token(Guid actual, string? received)
    {
        if (!Guid.TryParse(received, out var value) || value != actual)
            throw new ConflictException("The gate session script changed; reload before retrying.", "GATE_SESSION_STALE");
    }
    private async Task<ProjectGateInstance> Gate(long id, CancellationToken ct) =>
        await db.ProjectGateInstances.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new NotFoundException("ProjectGate", id);
    private async Task<string> Role(ProjectGateInstance gate, bool write, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct))
            throw new ForbiddenException("An active account is required.");
        var roles = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => a.GateInstanceId == gate.Id && a.ReviewerId == Actor && a.Status != "REVOKED")
            .Select(a => a.SessionRole).ToArrayAsync(ct);
        var role = roles.Contains("CHAIR") ? "CHAIR" : roles.Contains("SECRETARY") ? "SECRETARY" : "MEMBER";
        if (write && role == "MEMBER")
            throw new ForbiddenException("Only the assigned gate chair or secretary may edit the session script.");
        if (!write && roles.Length == 0)
        {
            var scope = await ProjectAcademicScopeReader.ReadAsync(db, gate.ProjectId, ct, true);
            var visible = await db.TeamMembers.AnyAsync(m => m.Team.Project != null && m.Team.Project.Id == gate.ProjectId
                    && m.UserId == Actor && m.LeftAt == null, ct)
                || await db.SupervisorAssignments.AnyAsync(a => a.ProjectId == gate.ProjectId && a.EndedAt == null
                    && a.SupervisorProfile.UserId == Actor, ct)
                || await db.Users.AnyAsync(u => u.Id == Actor && u.DepartmentId == scope.LeadDepartmentId
                    && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"), ct);
            if (!visible) throw new ForbiddenException("The gate session is outside the actor's project scope.");
        }
        return role;
    }
    private async Task<GateSessionScriptDto> Dto(ProjectGateInstance gate, GateSessionScript row, CancellationToken ct)
    {
        var roomCode = row.RoomId.HasValue ? await db.Set<Room>().AsNoTracking()
            .Where(r => r.Id == row.RoomId).Select(r => r.Code).SingleOrDefaultAsync(ct) : null;
        return new(row.Id, row.ProjectGateInstanceId, gate.ProjectId, gate.Code, gate.Title,
            row.Mode, row.RoomId, roomCode, row.LiveKitRoomName, row.LiveKitSessionId,
            row.ScheduledStartAt, row.ScheduledEndAt, row.ActualStartAt, row.ActualEndAt,
            row.TotalDurationMinutes, Parse<GatePresence>(row.PresencesJson),
            Parse<GateAgendaStage>(row.AgendaStagesJson), Parse<GateQaEntry>(row.QaLogJson),
            Parse<GateRemediationItem>(row.RemediationItemsJson), row.GeneralNotes, row.Verdict,
            row.SignedBySecretary, row.SignedByChair, row.ConcurrencyToken.ToString("N"),
            row.CreatedAt, row.UpdatedAt);
    }

    public async Task<GateSessionScriptDto> Get(long gateInstanceId, CancellationToken ct)
    {
        var gate = await Gate(gateInstanceId, ct);
        await Role(gate, false, ct);
        var row = await db.Set<GateSessionScript>().AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProjectGateInstanceId == gateInstanceId, ct)
            ?? throw new NotFoundException("GateSessionScript", gateInstanceId);
        return await Dto(gate, row, ct);
    }

    public async Task<GateSessionScriptDto> Save(long gateInstanceId, SaveGateSessionScriptRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var gate = await Gate(gateInstanceId, ct);
        var role = await Role(gate, true, ct);
        var scheduleLock = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (scheduleLock.Single() < 0) throw new ConflictException("The room schedule is busy; retry.", "DEFENSE_SCHEDULE_BUSY");
        if (input.Mode is not ("ONLINE" or "OFFLINE") || input.Verdict is not ("PENDING" or "PASSED" or "REVISION_REQUIRED" or "FAILED"))
            throw Invalid("sessionScript", "Mode or verdict is unsupported.");
        if (input.ScheduledEndAt <= input.ScheduledStartAt || input.ScheduledEndAt - input.ScheduledStartAt > TimeSpan.FromHours(12)
            || input.ActualStartAt.HasValue && input.ActualEndAt.HasValue && input.ActualEndAt <= input.ActualStartAt)
            throw Invalid("schedule", "Session times must be ordered and at most 12 hours apart.");
        if (input.Mode == "ONLINE" && (input.RoomId.HasValue || string.IsNullOrWhiteSpace(input.LiveKitRoomName)
                || input.LiveKitRoomName.Length > 100)
            || input.Mode == "OFFLINE" && (!input.RoomId.HasValue || !string.IsNullOrWhiteSpace(input.LiveKitRoomName)))
            throw Invalid("mode", "Online sessions require a LiveKit room; offline sessions require a physical room.");
        if (input.Mode == "OFFLINE" && !await db.Set<Room>().AnyAsync(r => r.Id == input.RoomId && r.Status == "ACTIVE", ct))
            throw Invalid("roomId", "The physical room is unavailable.");
        if (input.Mode == "OFFLINE")
        {
            var startUtc = input.ScheduledStartAt.UtcDateTime;
            var endUtc = input.ScheduledEndAt.UtcDateTime;
            if (await db.Set<DefenseSession>().AsNoTracking().AnyAsync(s => s.Status == "SCHEDULED"
                    && s.RoomId == input.RoomId && s.StartAt < endUtc && s.EndAt > startUtc, ct)
                || await db.Set<GateSessionScript>().AsNoTracking().AnyAsync(s => s.Mode == "OFFLINE"
                    && s.ProjectGateInstanceId != gate.Id && s.RoomId == input.RoomId
                    && s.ScheduledStartAt < input.ScheduledEndAt && s.ScheduledEndAt > input.ScheduledStartAt, ct))
                throw new ConflictException("The room is already booked for a defense or offline gate.", "DEFENSE_ROOM_CONFLICT");
        }
        if (input.Mode == "ONLINE" && !await db.MeetingVideoSessions.AsNoTracking()
            .AnyAsync(s => s.ProviderRoomKey == input.LiveKitRoomName && s.Provider == "LIVEKIT"
                && s.Meeting.ProjectId == gate.ProjectId && s.Status != "FAILED", ct))
            throw Invalid("liveKitRoomName", "A LiveKit room for this project must already be registered.");
        if (input.Mode == "ONLINE" && (input.ActualStartAt.HasValue || input.ActualEndAt.HasValue))
            throw Invalid("actualStartAt", "Online session times come from signed provider events.");
        if (input.Presences is null || input.AgendaStages is null || input.QaLog is null || input.RemediationItems is null
            || input.Presences.Count > 100 || input.AgendaStages.Count > 40 || input.QaLog.Count > 200
            || input.RemediationItems.Count > 100 || input.GeneralNotes?.Length > 10000
            || input.Presences.Select(p => p.UserId).Distinct().Count() != input.Presences.Count
            || input.Presences.Any(p => p.UserId <= 0 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 100
                || string.IsNullOrWhiteSpace(p.Role) || p.Role.Length > 40 || p.DurationMinutes < 0
                || p.LeftAt.HasValue && p.JoinedAt.HasValue && p.LeftAt < p.JoinedAt)
            || input.AgendaStages.Any(a => string.IsNullOrWhiteSpace(a.Stage) || a.Stage.Length > 200 || a.DurationMinutes is < 1 or > 720
                || a.Status is not ("PENDING" or "IN_PROGRESS" or "COMPLETED"))
            || input.QaLog.Any(q => string.IsNullOrWhiteSpace(q.Id) || q.Id.Length > 64 || string.IsNullOrWhiteSpace(q.Question)
                || q.Question.Length > 4000 || q.Evaluation is not ("SATISFACTORY" or "ACCEPTABLE" or "NEEDS_REVISION"))
            || input.RemediationItems.Any(r => string.IsNullOrWhiteSpace(r.Id) || string.IsNullOrWhiteSpace(r.Title)
                || r.Title.Length > 1000 || string.IsNullOrWhiteSpace(r.Assignee) || r.Assignee.Length > 200
                || r.WorkingDaysToComplete is < 1 or > 365))
            throw Invalid("sessionScript", "The script contains invalid or excessive content.");
        var periodId = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectId == gate.ProjectId).OrderByDescending(s => s.Id)
            .Select(s => (long?)s.ProjectPeriodId).FirstOrDefaultAsync(ct);
        var calendar = periodId.HasValue ? await db.Set<ProjectPeriodCalendar>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProjectPeriodId == periodId, ct) : null;
        var holidays = calendar is null ? [] : Parse<PeriodHoliday>(calendar.HolidaysJson);
        var remediation = input.RemediationItems.Select(r => r.WorkingDaysToComplete.HasValue && periodId.HasValue
            ? r with { DueDate = DateOnly.FromDateTime(workingDays.AddWorkingDays(
                input.ActualEndAt ?? input.ScheduledEndAt, r.WorkingDaysToComplete.Value, periodId.Value).Date) }
            : r).ToArray();
        if (calendar is not null && remediation.Any(r => !WorkingDayMath.IsWorkingDay(r.DueDate,
                calendar?.WorkingDaysOfWeek ?? 62, holidays)))
            throw Invalid("remediationItems", "Every remediation deadline must be a configured working day.");
        GatePresence[] offlinePresence = [];
        if (input.Mode == "OFFLINE")
        {
            var reviewerIds = await db.GateReviewerAssignments.AsNoTracking()
                .Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED")
                .Select(a => a.ReviewerId).ToArrayAsync(ct);
            var memberIds = await db.TeamMembers.AsNoTracking().Where(m => m.Team.Project != null
                && m.Team.Project.Id == gate.ProjectId && m.LeftAt == null).Select(m => m.UserId).ToArrayAsync(ct);
            var approved = reviewerIds.Concat(memberIds).Distinct().ToHashSet();
            if (input.Presences.Any(p => !approved.Contains(p.UserId)))
                throw new ForbiddenException("Offline presence contains a participant outside this gate.");
            var names = await db.Users.AsNoTracking().Where(u => approved.Contains(u.Id) && u.Status == "ACTIVE")
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
            if (input.Presences.Any(p => !names.ContainsKey(p.UserId)))
                throw new ForbiddenException("Offline presence contains an inactive participant.");
            offlinePresence = input.Presences.Select(p => p with { Name = names[p.UserId],
                Role = reviewerIds.Contains(p.UserId) ? "REVIEWER" : "STUDENT" }).ToArray();
        }
        if (input.Verdict != "PENDING" && (string.IsNullOrWhiteSpace(input.SignedBySecretary) || string.IsNullOrWhiteSpace(input.SignedByChair)))
            throw Invalid("signatures", "Both chair and secretary signatures are required for a final verdict.");
        var actorName = await db.Users.AsNoTracking().Where(u => u.Id == Actor).Select(u => u.FullName).SingleAsync(ct);
        if (role == "CHAIR" && !string.IsNullOrWhiteSpace(input.SignedByChair) && input.SignedByChair != actorName
            || role == "SECRETARY" && !string.IsNullOrWhiteSpace(input.SignedBySecretary) && input.SignedBySecretary != actorName)
            throw new ForbiddenException("A participant may only sign with their own account name.");
        var row = await db.Set<GateSessionScript>().SingleOrDefaultAsync(s => s.ProjectGateInstanceId == gate.Id, ct);
        if (row is null)
        {
            if (!string.IsNullOrWhiteSpace(input.ConcurrencyToken)) throw new ConflictException("The session has not been created yet.");
            if (role != "CHAIR" && !string.IsNullOrWhiteSpace(input.SignedByChair)
                || role != "SECRETARY" && !string.IsNullOrWhiteSpace(input.SignedBySecretary))
                throw new ForbiddenException("Another participant's signature cannot be created.");
            row = new GateSessionScript { ProjectGateInstanceId = gate.Id, CreatedAt = Now };
            db.Set<GateSessionScript>().Add(row);
        }
        else
        {
            Token(row.ConcurrencyToken, input.ConcurrencyToken);
            if (role != "CHAIR" && input.SignedByChair != row.SignedByChair
                || role != "SECRETARY" && input.SignedBySecretary != row.SignedBySecretary)
                throw new ForbiddenException("Another participant's signature cannot be changed.");
            if (row.Mode == "ONLINE" && row.LiveKitSessionId is not null
                && (input.Mode != row.Mode || input.LiveKitRoomName != row.LiveKitRoomName))
                throw new ConflictException("A synchronized LiveKit session cannot be rebound.");
        }
        var previousMode = row.Mode;
        row.Mode = input.Mode; row.RoomId = input.RoomId;
        row.LiveKitRoomName = input.LiveKitRoomName?.Trim();
        if (previousMode != "ONLINE" && row.Mode == "ONLINE")
        {
            row.ActualStartAt = null; row.ActualEndAt = null;
            row.TotalDurationMinutes = null; row.PresencesJson = "[]";
        }
        row.ScheduledStartAt = input.ScheduledStartAt; row.ScheduledEndAt = input.ScheduledEndAt;
        if (row.Mode == "OFFLINE")
        {
            row.ActualStartAt = input.ActualStartAt; row.ActualEndAt = input.ActualEndAt;
            row.TotalDurationMinutes = input.ActualStartAt.HasValue && input.ActualEndAt.HasValue
                ? (int)(input.ActualEndAt.Value - input.ActualStartAt.Value).TotalMinutes : null;
            row.PresencesJson = JsonSerializer.Serialize(offlinePresence);
        }
        row.AgendaStagesJson = JsonSerializer.Serialize(input.AgendaStages);
        row.QaLogJson = JsonSerializer.Serialize(input.QaLog);
        row.RemediationItemsJson = JsonSerializer.Serialize(remediation);
        row.GeneralNotes = input.GeneralNotes?.Trim(); row.Verdict = input.Verdict;
        row.SignedBySecretary = input.SignedBySecretary; row.SignedByChair = input.SignedByChair;
        row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "GATE_SESSION_SCRIPT_SAVED", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = gate.ProjectId, ["mode"] = row.Mode,
                ["verdict"] = row.Verdict }), ct);
        await tx.CommitAsync(ct);
        return await Dto(gate, row, ct);
    }

    public async Task<GateSessionScriptDto> Sync(long gateInstanceId, SyncGatePresenceRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var gate = await Gate(gateInstanceId, ct);
        await Role(gate, true, ct);
        var row = await db.Set<GateSessionScript>().SingleOrDefaultAsync(s => s.ProjectGateInstanceId == gate.Id, ct)
            ?? throw new NotFoundException("GateSessionScript", gate.Id);
        if (row.Mode != "ONLINE") throw new ConflictException("LiveKit presence requires an online gate session.");
        var providerSession = await db.MeetingVideoSessions.AsNoTracking()
            .SingleOrDefaultAsync(s => s.ProviderRoomKey == row.LiveKitRoomName && s.Provider == "LIVEKIT"
                && s.Meeting.ProjectId == gate.ProjectId && s.Status != "FAILED", ct)
            ?? throw new ConflictException("The linked LiveKit session is unavailable.", "GATE_LIVEKIT_SESSION_MISSING");
        var providerSessionId = providerSession.Id.ToString(CultureInfo.InvariantCulture);
        if (input.SessionId != providerSessionId || row.LiveKitSessionId is not null
            && row.LiveKitSessionId != providerSessionId)
            throw Invalid("sessionId", "The session id does not match the registered LiveKit session.");
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (input.Participants is null || input.Participants.Count > 100
            || input.Participants.Select(p => p.UserId).Distinct().Count() != input.Participants.Count)
            throw Invalid("participants", "At most 100 distinct expected participants are allowed.");
        var providerPresence = await db.MeetingVideoPresenceSessions.AsNoTracking()
            .Where(p => p.MeetingVideoSessionId == providerSession.Id).ToArrayAsync(ct);
        if (input.Participants.Any(p => !providerPresence.Any(v => v.UserId == p.UserId)))
            throw new ConflictException("A submitted participant has no signed LiveKit presence event.", "GATE_PRESENCE_UNVERIFIED");
        var allowed = await db.GateReviewerAssignments.AsNoTracking().Where(a => a.GateInstanceId == gate.Id && a.Status != "REVOKED")
            .Select(a => a.ReviewerId).ToArrayAsync(ct);
        var members = await db.TeamMembers.AsNoTracking().Where(m => m.Team.Project != null
            && m.Team.Project.Id == gate.ProjectId && m.LeftAt == null).Select(m => m.UserId).ToArrayAsync(ct);
        var ids = allowed.Concat(members).Distinct().ToHashSet();
        if (providerPresence.Any(p => !ids.Contains(p.UserId)))
            throw new ForbiddenException("Presence contains a participant outside this gate.");
        var existing = Parse<GatePresence>(row.PresencesJson).ToDictionary(p => p.UserId);
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.Status == "ACTIVE")
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        if (providerPresence.Any(p => !names.ContainsKey(p.UserId)))
            throw new ForbiddenException("Presence contains an inactive participant.");
        foreach (var group in providerPresence.GroupBy(p => p.UserId))
        {
            var joined = group.Min(p => p.JoinedAt);
            var left = group.Any(p => p.LeftAt is null) ? (DateTime?)null : group.Max(p => p.LeftAt);
            var duration = group.Sum(p => Math.Max(0, (int)((p.LeftAt ?? providerSession.EndedAt ?? Now.UtcDateTime) - p.JoinedAt).TotalMinutes));
            if (joined > Now.UtcDateTime || left < joined) continue;
            existing[group.Key] = new GatePresence(group.Key, names[group.Key],
                allowed.Contains(group.Key) ? "REVIEWER" : "STUDENT", true,
                new DateTimeOffset(DateTime.SpecifyKind(joined, DateTimeKind.Utc)),
                left.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(left.Value, DateTimeKind.Utc)) : null,
                duration);
        }
        row.PresencesJson = JsonSerializer.Serialize(existing.Values.OrderBy(p => p.UserId));
        row.LiveKitSessionId = providerSessionId;
        row.ActualStartAt = providerSession.StartedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(providerSession.StartedAt.Value, DateTimeKind.Utc)) : null;
        row.ActualEndAt = providerSession.EndedAt.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(providerSession.EndedAt.Value, DateTimeKind.Utc)) : null;
        row.TotalDurationMinutes = row.ActualStartAt.HasValue && row.ActualEndAt.HasValue
            ? (int)(row.ActualEndAt.Value - row.ActualStartAt.Value).TotalMinutes : null;
        row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "GATE_SESSION_PRESENCE_SYNCED", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["projectId"] = gate.ProjectId, ["sessionId"] = providerSessionId,
                ["participantCount"] = providerPresence.Length }), ct);
        await tx.CommitAsync(ct);
        return await Dto(gate, row, ct);
    }
}
