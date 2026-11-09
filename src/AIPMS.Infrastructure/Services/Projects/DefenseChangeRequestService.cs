using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class DefenseChangeRequestService(AipmsDbContext db, ICurrentUser current, IAuditTrail audit,
    TimeProvider clock) : IDefenseChangeRequestService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static ValidationException Invalid(string value) => new(new Dictionary<string, string[]> { ["changeRequest"] = [value] });
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static DefenseChangeRequestDto Dto(DefenseScheduleChangeRequest row) => new(row.Id, row.DefenseSessionId,
        row.RequesterId, row.CurrentRoomId, row.CurrentStartAt, row.CurrentEndAt, row.DesiredRoomId,
        row.DesiredStartAt, row.DesiredEndAt, row.ReasonCategory, row.ReasonDetails, row.EvidenceFileUrl,
        row.Status, row.ReviewerId, row.ReviewNote, row.ReviewedAt, row.ConcurrencyToken.ToString("N"), row.CreatedAt);
    private static void Token(Guid actual, string value)
    {
        if (!Guid.TryParse(value, out var received) || received != actual)
            throw new ConflictException("The change request changed; reload before retrying.", "DEFENSE_CHANGE_STALE");
    }
    private async Task Lock(CancellationToken ct)
    {
        var result = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (result.Single() < 0) throw new ConflictException("Defense schedule is busy; retry.", "DEFENSE_SCHEDULE_BUSY");
    }
    private async Task<DefenseSession> Session(long id, CancellationToken ct) =>
        await db.Set<DefenseSession>().SingleOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("DefenseSession", id);
    private async Task<DefenseScheduleChangeRequest> Request(long id, CancellationToken ct) =>
        await db.Set<DefenseScheduleChangeRequest>().SingleOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException("DefenseScheduleChangeRequest", id);
    private async Task<long> Period(long projectId, CancellationToken ct) =>
        await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => (long?)s.ProjectPeriodId).FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("A frozen project period is required.", "ACADEMIC_SCOPE_UNKNOWN");
    private async Task<long> LeadDepartment(long projectId, CancellationToken ct) =>
        await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => (long?)s.LeadDepartmentId).FirstOrDefaultAsync(ct)
            ?? throw new ConflictException("A frozen lead department is required.", "ACADEMIC_SCOPE_UNKNOWN");
    private async Task RequireLeadStaff(long projectId, CancellationToken ct)
    {
        var department = await LeadDepartment(projectId, ct);
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE"
            && (u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN")
                || u.DepartmentId == department && u.Department != null && u.Department.IsActive
                    && u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF")), ct))
            throw new ForbiddenException("Only Admin or academic staff of the lead department may decide this request.");
    }
    private async Task ValidateTimeAndRoom(DefenseSession session, long roomId, DateTimeOffset start,
        DateTimeOffset end, CancellationToken ct)
    {
        var local = start.ToOffset(VietnamOffset);
        var date = DateOnly.FromDateTime(local.Date);
        if (start.UtcDateTime <= Now || end <= start || date.DayOfWeek == DayOfWeek.Sunday)
            throw Invalid("A future non-Sunday office slot is required.");
        var validSlot = Enumerable.Range(1, 6).Any(number => {
            var slot = DefenseSchedulingRules.Slot(date, number);
            return start == slot.Start && end == slot.End;
        });
        if (!validSlot) throw Invalid("The desired session must match one of the six office slots.");
        await OfflineGateEligibility.RequirePreviousGateAndWindow(db, session.ProjectId, 4, start, end, ct);
        var periodId = await Period(session.ProjectId, ct);
        var calendar = await db.Set<ProjectPeriodCalendar>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectPeriodId == periodId, ct);
        if (calendar is not null && !WorkingDayMath.IsWorkingDay(date, calendar.WorkingDaysOfWeek,
            JsonSerializer.Deserialize<PeriodHoliday[]>(calendar.HolidaysJson) ?? []))
            throw new ConflictException("The desired date is a configured non-working day.", "DEFENSE_NON_WORKING_DAY");
        var room = await db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == roomId && r.Status == "ACTIVE", ct)
            ?? throw new ConflictException("The desired room is unavailable.", "DEFENSE_ROOM_UNAVAILABLE");
        var groupSize = await db.Projects.AsNoTracking().Where(p => p.Id == session.ProjectId)
            .Select(p => p.Team.TeamMembers.Count(m => m.LeftAt == null)).SingleAsync(ct);
        var members = await db.Set<DefenseCommitteeMember>().AsNoTracking().Where(m => m.CommitteeId == session.CommitteeId)
            .Select(m => m.UserId).Distinct().ToArrayAsync(ct);
        if (room.Capacity < groupSize + members.Length + 5)
            throw new ConflictException("The room has insufficient capacity.", "DEFENSE_ROOM_CAPACITY");
        var startUtc = start.UtcDateTime; var endUtc = end.UtcDateTime;
        var overlapping = await db.Set<DefenseSession>().AsNoTracking().Where(s => s.Id != session.Id && s.Status == "SCHEDULED"
            && s.StartAt < endUtc && s.EndAt > startUtc).ToArrayAsync(ct);
        if (overlapping.Any(s => s.RoomId == roomId || s.RoomId is null && s.Room == room.Code))
            throw new ConflictException("The desired room is already occupied.", "DEFENSE_ROOM_CONFLICT");
        if (await db.Set<GateSessionScript>().AsNoTracking().AnyAsync(s => s.Mode == "OFFLINE" && s.RoomId == roomId
            && s.ScheduledStartAt < end && s.ScheduledEndAt > start, ct))
            throw new ConflictException("The desired room is occupied by an offline review gate.", "DEFENSE_ROOM_CONFLICT");
        if (await db.Set<GateSessionScript>().AsNoTracking().AnyAsync(s => s.Mode == "OFFLINE"
            && s.ScheduledStartAt < end && s.ScheduledEndAt > start
            && (db.ProjectGateInstances.Any(g => g.Id == s.ProjectGateInstanceId && g.ProjectId == session.ProjectId)
                || db.GateReviewerAssignments.Any(a => a.GateInstanceId == s.ProjectGateInstanceId
                    && a.Status != "REVOKED" && members.Contains(a.ReviewerId))), ct))
            throw new ConflictException("The group or committee member has an overlapping offline review.",
                "DEFENSE_LECTURER_CONFLICT");
        var committeeIds = overlapping.Select(s => s.CommitteeId).Distinct().ToArray();
        if (await db.Set<DefenseCommitteeMember>().AsNoTracking().AnyAsync(m => committeeIds.Contains(m.CommitteeId)
            && members.Contains(m.UserId), ct))
            throw new ConflictException("A committee member has another defense in this slot.", "DEFENSE_LECTURER_CONFLICT");
    }

    public async Task<DefenseChangeRequestDto> Create(long sessionId, CreateDefenseChangeRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var session = await Session(sessionId, ct);
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct)) throw new ForbiddenException();
        if (!await db.Set<DefenseCommitteeMember>().AnyAsync(m => m.CommitteeId == session.CommitteeId && m.UserId == Actor
            && (m.Role == "CHAIR" || m.Role == "SECRETARY"), ct))
            throw new ForbiddenException("Only the assigned chair or secretary may request a schedule change.");
        if (session.Status != "SCHEDULED" || session.RoomId is null || session.EndAt <= Now)
            throw new ConflictException("Only a future cataloged defense can be changed.", "DEFENSE_SESSION_INACTIVE");
        if (input.ReasonCategory is not ("ROOM_FACILITY_ISSUE" or "SCHEDULE_CLASH" or "SPECIAL_REQUIREMENT" or "OTHER")
            || string.IsNullOrWhiteSpace(input.ReasonDetails) || input.ReasonDetails.Length > 2000
            || input.EvidenceFileUrl?.Length > 500
            || input.EvidenceFileUrl is not null && !input.EvidenceFileUrl.StartsWith("/api/v1/files/", StringComparison.Ordinal)
                && (!Uri.TryCreate(input.EvidenceFileUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw Invalid("A valid category, reason and trusted evidence URL are required.");
        if (session.RoomId == input.DesiredRoomId && Utc(session.StartAt) == input.DesiredStartAt
            && Utc(session.EndAt) == input.DesiredEndAt) throw Invalid("The requested schedule must change.");
        if (await db.Set<DefenseScheduleChangeRequest>().AnyAsync(r => r.DefenseSessionId == sessionId && r.Status == "PENDING", ct))
            throw new ConflictException("A pending change request already exists.", "DEFENSE_CHANGE_PENDING");
        await ValidateTimeAndRoom(session, input.DesiredRoomId, input.DesiredStartAt, input.DesiredEndAt, ct);
        var row = new DefenseScheduleChangeRequest { DefenseSessionId = sessionId, RequesterId = Actor,
            CurrentRoomId = session.RoomId.Value, CurrentStartAt = Utc(session.StartAt), CurrentEndAt = Utc(session.EndAt),
            DesiredRoomId = input.DesiredRoomId, DesiredStartAt = input.DesiredStartAt, DesiredEndAt = input.DesiredEndAt,
            ReasonCategory = input.ReasonCategory, ReasonDetails = input.ReasonDetails.Trim(),
            EvidenceFileUrl = input.EvidenceFileUrl, CreatedAt = Utc(Now) };
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "DEFENSE_CHANGE_REQUESTED", "DEFENSE_CHANGE_REQUEST", row.Id,
            new Dictionary<string, object?> { ["sessionId"] = sessionId, ["desiredRoomId"] = row.DesiredRoomId }), ct);
        await tx.CommitAsync(ct); return Dto(row);
    }

    public async Task<DefenseChangeRequestDto> Decide(long id, DecideDefenseChangeRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var row = await Request(id, ct); var session = await Session(row.DefenseSessionId, ct);
        await RequireLeadStaff(session.ProjectId, ct);
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status != "PENDING") throw new ConflictException("The request is no longer pending.", "DEFENSE_CHANGE_CLOSED");
        if (input.Decision is not ("APPROVED" or "REJECTED") || input.ReviewNote?.Length > 2000)
            throw Invalid("Decision must be APPROVED or REJECTED with a bounded review note.");
        if (input.Decision == "APPROVED")
        {
            if (session.Status != "SCHEDULED" || session.RoomId != row.CurrentRoomId
                || Utc(session.StartAt) != row.CurrentStartAt || Utc(session.EndAt) != row.CurrentEndAt)
                throw new ConflictException("The original defense changed; submit a new request.", "DEFENSE_SESSION_STALE");
            await ValidateTimeAndRoom(session, row.DesiredRoomId, row.DesiredStartAt, row.DesiredEndAt, ct);
            var room = await db.Set<Room>().AsNoTracking().SingleAsync(r => r.Id == row.DesiredRoomId, ct);
            session.RoomId = room.Id; session.Room = room.Code;
            session.StartAt = row.DesiredStartAt.UtcDateTime; session.EndAt = row.DesiredEndAt.UtcDateTime;
            session.ConcurrencyToken = Guid.NewGuid();
        }
        row.Status = input.Decision; row.ReviewerId = Actor; row.ReviewNote = input.ReviewNote?.Trim();
        row.ReviewedAt = Utc(Now); row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        if (input.Decision == "APPROVED")
            await ScheduleNotificationQueue.Defense(db, Actor, session.ProjectId, session.CommitteeId,
                session.Id, Now, true, ct);
        await audit.RecordAsync(new(Actor, "DEFENSE_CHANGE_" + input.Decision, "DEFENSE_CHANGE_REQUEST", row.Id,
            new Dictionary<string, object?> { ["sessionId"] = session.Id, ["oldRoomId"] = row.CurrentRoomId,
                ["newRoomId"] = row.DesiredRoomId, ["reviewNote"] = row.ReviewNote }), ct);
        await tx.CommitAsync(ct); return Dto(row);
    }

    public async Task<DefenseChangeRequestDto> Withdraw(long id, WithdrawDefenseChangeRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var row = await Request(id, ct);
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == Actor && u.Status == "ACTIVE", ct)) throw new ForbiddenException();
        if (row.RequesterId != Actor) throw new ForbiddenException("Only the requester may withdraw this change.");
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        if (row.Status != "PENDING") throw new ConflictException("The request is no longer pending.", "DEFENSE_CHANGE_CLOSED");
        row.Status = "WITHDRAWN"; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "DEFENSE_CHANGE_WITHDRAWN", "DEFENSE_CHANGE_REQUEST", row.Id,
            new Dictionary<string, object?> { ["sessionId"] = row.DefenseSessionId }), ct);
        await tx.CommitAsync(ct); return Dto(row);
    }

    public async Task<IReadOnlyList<DefenseChangeRequestDto>> List(long periodId, string? status, CancellationToken ct)
    {
        if (!await db.ProjectPeriods.AnyAsync(p => p.Id == periodId, ct)) throw new NotFoundException("ProjectPeriod", periodId);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF"),
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN") })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        if (!user.Admin && (!user.Staff || !user.DepartmentId.HasValue)) throw new ForbiddenException();
        if (status is not null and not ("PENDING" or "APPROVED" or "REJECTED" or "WITHDRAWN"))
            throw Invalid("Unknown change request status.");
        var projects = db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectPeriodId == periodId
            && (user.Admin || s.LeadDepartmentId == user.DepartmentId)).Select(s => s.ProjectId).Distinct();
        var sessions = db.Set<DefenseSession>().AsNoTracking().Where(s => projects.Contains(s.ProjectId)).Select(s => s.Id);
        var query = db.Set<DefenseScheduleChangeRequest>().AsNoTracking().Where(r => sessions.Contains(r.DefenseSessionId));
        if (status is not null) query = query.Where(r => r.Status == status);
        return (await query.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).ToArrayAsync(ct)).Select(Dto).ToArray();
    }
}
