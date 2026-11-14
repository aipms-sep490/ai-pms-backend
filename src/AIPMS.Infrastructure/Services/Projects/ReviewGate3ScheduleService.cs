using System.Security.Cryptography;
using System.Text;
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

internal sealed class ReviewGate3ScheduleService(AipmsDbContext db, ICurrentUser current,
    IAuditTrail audit, TimeProvider clock) : IReviewGate3ScheduleService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTimeOffset Now => clock.GetUtcNow();
    private sealed record Candidate(long ProjectId, long GateId, long? DepartmentId, int Seats,
        long[] Reviewers, long[] Supervisors, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);
    private sealed record Occupied(long ProjectId, long? GateId, long? RoomId, string? RoomCode,
        DateTimeOffset Start, DateTimeOffset End, long[] Reviewers);
    private static ValidationException Invalid(string message) => new(new Dictionary<string, string[]> { ["reviewGate3Schedule"] = [message] });

    private async Task<long?> Department(long periodId, CancellationToken ct)
    {
        var organization = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == periodId)
            .Select(p => (long?)p.AcademicSemester.OrganizationId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Organization = u.Department == null ? (long?)null : u.Department.OrganizationId,
                ActiveDepartment = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException();
        if (user.Admin) return null;
        if (!user.Staff || !user.ActiveDepartment || user.Organization != organization || !user.DepartmentId.HasValue)
            throw new ForbiddenException();
        return user.DepartmentId;
    }

    private async Task<string> Revision(CancellationToken ct)
    {
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => g.Sequence == 2 || g.Sequence == 3)
            .OrderBy(g => g.Id).Select(g => new { g.Id, g.ConcurrencyToken }).ToArrayAsync(ct);
        var sessions = await db.Set<GateSessionScript>().AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.ConcurrencyToken }).ToArrayAsync(ct);
        var defenses = await db.Set<DefenseSession>().AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.ConcurrencyToken }).ToArrayAsync(ct);
        var rooms = await db.Set<Room>().AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.ConcurrencyToken }).ToArrayAsync(ct);
        var reviewers = await db.GateReviewerAssignments.AsNoTracking().OrderBy(a => a.Id)
            .Select(a => new { a.Id, a.GateInstanceId, a.ReviewerId, a.SessionRole, a.Status }).ToArrayAsync(ct);
        var value = JsonSerializer.Serialize(new { gates, sessions, defenses, rooms, reviewers });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private async Task<(Dictionary<long, Candidate> Candidates, DefenseScheduleConflict[] Issues)> Candidates(
        long periodId, long? departmentId, CancellationToken ct)
    {
        var snapshots = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectPeriodId == periodId && (!departmentId.HasValue || s.LeadDepartmentId == departmentId))
            .OrderByDescending(s => s.Id).ToArrayAsync(ct);
        var ids = snapshots.Select(s => s.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .Where(p => ids.Contains(p.Id) && (p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION"))
            .ToArrayAsync(ct);
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => ids.Contains(g.ProjectId)
            && (g.Sequence == 2 || g.Sequence == 3)).ToArrayAsync(ct);
        var gateIds = gates.Where(g => g.Sequence == 3).Select(g => g.Id).ToArray();
        var reviewers = await db.GateReviewerAssignments.AsNoTracking()
            .Where(a => gateIds.Contains(a.GateInstanceId) && a.Status != "REVOKED")
            .Select(a => new { a.GateInstanceId, a.ReviewerId, a.SessionRole }).ToArrayAsync(ct);
        var activeReviewerIds = (await db.Users.AsNoTracking().Where(u => u.Status == "ACTIVE"
            && reviewers.Select(r => r.ReviewerId).Contains(u.Id)).Select(u => u.Id).ToArrayAsync(ct)).ToHashSet();
        var supervisors = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => ids.Contains(a.ProjectId) && a.EndedAt == null)
            .Select(a => new { a.ProjectId, a.SupervisorProfile.UserId }).ToArrayAsync(ct);
        var results = new Dictionary<long, Candidate>();
        var issues = new List<DefenseScheduleConflict>();
        foreach (var project in projects)
        {
            if (!gates.Any(g => g.ProjectId == project.Id && g.Sequence == 2 && g.Status == "PASSED"))
            { issues.Add(new(project.Id, "REVIEW_GATE_2_NOT_PASSED")); continue; }
            var gate = gates.SingleOrDefault(g => g.ProjectId == project.Id && g.Sequence == 3);
            if (gate?.WindowStart is null || gate.WindowEnd is null || gate.WindowEnd <= gate.WindowStart)
            { issues.Add(new(project.Id, "REVIEW_GATE_3_WINDOW_REQUIRED")); continue; }
            if (gate.Status is "PASSED" or "FAILED" or "REJECTED")
            { issues.Add(new(project.Id, "REVIEW_GATE_3_CLOSED")); continue; }
            var assigned = reviewers.Where(r => r.GateInstanceId == gate.Id
                && activeReviewerIds.Contains(r.ReviewerId)).ToArray();
            if (assigned.Where(r => r.SessionRole == "CHAIR").Select(r => r.ReviewerId).Distinct().Count() != 1
                || assigned.Where(r => r.SessionRole == "SECRETARY").Select(r => r.ReviewerId).Distinct().Count() != 1
                || assigned.FirstOrDefault(r => r.SessionRole == "CHAIR")?.ReviewerId
                    == assigned.FirstOrDefault(r => r.SessionRole == "SECRETARY")?.ReviewerId
                || assigned.Select(r => r.ReviewerId).Distinct().Count() != 2)
            { issues.Add(new(project.Id, "REVIEW_GATE_3_REVIEWERS_REQUIRED")); continue; }
            var idsForGate = assigned.Select(r => r.ReviewerId).Distinct().ToArray();
            var supervisorsForProject = supervisors.Where(s => s.ProjectId == project.Id).Select(s => s.UserId).Distinct().ToArray();
            if (idsForGate.Intersect(supervisorsForProject).Any())
            { issues.Add(new(project.Id, "SUPERVISOR_CONFLICT_OF_INTEREST")); continue; }
            results.Add(project.Id, new(project.Id, gate.Id,
                snapshots.First(s => s.ProjectId == project.Id).LeadDepartmentId,
                project.Team.TeamMembers.Count(m => m.LeftAt == null) + idsForGate.Length + 5,
                idsForGate, supervisorsForProject,
                new(DateTime.SpecifyKind(gate.WindowStart.Value, DateTimeKind.Utc)),
                new(DateTime.SpecifyKind(gate.WindowEnd.Value, DateTimeKind.Utc))));
        }
        return (results, issues.ToArray());
    }

    private async Task<List<Occupied>> Existing(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var scripts = await db.Set<GateSessionScript>().AsNoTracking().Where(s => s.Mode == "OFFLINE"
            && s.ScheduledStartAt < to && s.ScheduledEndAt > from).ToArrayAsync(ct);
        var gateIds = scripts.Select(s => s.ProjectGateInstanceId).ToArray();
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => gateIds.Contains(g.Id))
            .Select(g => new { g.Id, g.ProjectId }).ToDictionaryAsync(g => g.Id, ct);
        var reviewers = await db.GateReviewerAssignments.AsNoTracking().Where(a => gateIds.Contains(a.GateInstanceId)
            && a.Status != "REVOKED").Select(a => new { a.GateInstanceId, a.ReviewerId }).ToArrayAsync(ct);
        var result = scripts.Where(s => gates.ContainsKey(s.ProjectGateInstanceId)).Select(s =>
            new Occupied(gates[s.ProjectGateInstanceId].ProjectId, s.ProjectGateInstanceId, s.RoomId, null,
                s.ScheduledStartAt, s.ScheduledEndAt,
                reviewers.Where(r => r.GateInstanceId == s.ProjectGateInstanceId).Select(r => r.ReviewerId).ToArray())).ToList();
        var defenses = await db.Set<DefenseSession>().AsNoTracking().Where(s => s.Status == "SCHEDULED"
            && s.StartAt < to.UtcDateTime && s.EndAt > from.UtcDateTime).ToArrayAsync(ct);
        var committeeIds = defenses.Select(s => s.CommitteeId).ToArray();
        var members = await db.Set<DefenseCommitteeMember>().AsNoTracking().Where(m => committeeIds.Contains(m.CommitteeId))
            .Select(m => new { m.CommitteeId, m.UserId }).ToArrayAsync(ct);
        result.AddRange(defenses.Select(s => new Occupied(s.ProjectId, null, s.RoomId, s.Room,
            new(DateTime.SpecifyKind(s.StartAt, DateTimeKind.Utc)),
            new(DateTime.SpecifyKind(s.EndAt, DateTimeKind.Utc)),
            members.Where(m => m.CommitteeId == s.CommitteeId).Select(m => m.UserId).ToArray())));
        return result;
    }

    private static bool Clash(Candidate candidate, Room room, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyList<Occupied> occupied) => occupied.Any(x => x.Start < end && x.End > start
            && (x.ProjectId == candidate.ProjectId || x.RoomId == room.Id
                || x.RoomId is null && x.RoomCode == room.Code
                || x.Reviewers.Intersect(candidate.Reviewers).Any()));
    private static bool Excluded(long roomId, DateOnly date, int slot,
        IReadOnlyList<RoomSchedulingExclusion>? exclusions) => exclusions?.Any(x => x.RoomId == roomId
            && (!x.Date.HasValue || x.Date == date) && (!x.SlotNumber.HasValue || x.SlotNumber == slot)) == true;
    private static void ValidateExclusions(IReadOnlyList<RoomSchedulingExclusion>? exclusions,
        IReadOnlyList<long>? roomIds, DateOnly? start, DateOnly? end)
    {
        if (exclusions is null) return;
        if (exclusions.Count > 1000 || exclusions.Distinct().Count() != exclusions.Count
            || exclusions.Any(x => x.RoomId <= 0 || roomIds is not null && !roomIds.Contains(x.RoomId)
                || x.SlotNumber is < 1 or > 6 || x.Date?.Year > 2200
                || x.Date.HasValue && start.HasValue && end.HasValue
                    && (x.Date < start || x.Date > end)))
            throw Invalid("Exclusions must name selected rooms and slots in this run only.");
    }
    private async Task<(int Mask, PeriodHoliday[] Holidays)> Calendar(long periodId, CancellationToken ct)
    {
        var row = await db.Set<ProjectPeriodCalendar>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.ProjectPeriodId == periodId, ct);
        return row is null ? (62, []) : (row.WorkingDaysOfWeek,
            JsonSerializer.Deserialize<PeriodHoliday[]>(row.HolidaysJson) ?? []);
    }
    private static bool Working(DateOnly date, int mask, PeriodHoliday[] holidays) =>
        date.DayOfWeek != DayOfWeek.Sunday && WorkingDayMath.IsWorkingDay(date, mask, holidays);
    private async Task Lock(CancellationToken ct)
    {
        var result = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (result.Single() < 0) throw new ConflictException("Room schedule is busy; retry.", "DEFENSE_SCHEDULE_BUSY");
    }
    private static ReviewGate3ScheduleDto Dto(ProjectGateInstance gate, GateSessionScript script, Room room) =>
        new(gate.ProjectId, gate.Id, room.Id, room.Code, script.ScheduledStartAt,
            script.ScheduledEndAt, script.PublishedAt!.Value, script.PublishedBy,
            script.ConcurrencyToken.ToString("N"));

    public async Task<ReviewGate3SchedulePreviewDto> Preview(long periodId, AutoSchedulePreviewRequest input, CancellationToken ct)
    {
        var department = await Department(periodId, ct);
        if (input.StartDate > input.EndDate || input.EndDate.DayNumber - input.StartDate.DayNumber > 31
            || input.EndDate.Year > 2200 || input.RoomIds is null || input.RoomIds.Count is < 1 or > 100
            || input.RoomIds.Any(id => id <= 0) || input.RoomIds.Distinct().Count() != input.RoomIds.Count
            || input.MaxSlotsPerDay is < 1 or > 6)
            throw Invalid("Use up to 32 days, 1..100 unique rooms and 1..6 office slots per day.");
        ValidateExclusions(input.Exclusions, input.RoomIds, input.StartDate, input.EndDate);
        var from = DefenseSchedulingRules.Slot(input.StartDate, 1).Start;
        var to = DefenseSchedulingRules.Slot(input.EndDate, 6).End;
        if (from <= Now) throw Invalid("Review dates must be in the future.");
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => input.RoomIds.Contains(r.Id)
            && r.Status == "ACTIVE" && r.Floor >= 2 && r.Floor <= 4).ToDictionaryAsync(r => r.Id, ct);
        if (rooms.Count != input.RoomIds.Count)
            throw new ConflictException("Only active rooms on floors 2–4 may be selected.", "REVIEW_GATE_3_ROOM_UNAVAILABLE");
        var (candidates, issues) = await Candidates(periodId, department, ct);
        var occupied = await Existing(from, to, ct);
        var already = (await db.Set<GateSessionScript>().AsNoTracking().Where(s => s.PublishedAt != null)
            .Select(s => s.ProjectGateInstanceId).ToArrayAsync(ct)).ToHashSet();
        var calendar = await Calendar(periodId, ct);
        var allocations = new List<ReviewGate3Allocation>(); var conflicts = new List<DefenseScheduleConflict>();
        var publishedProjects = await db.ProjectGateInstances.AsNoTracking()
            .Where(g => already.Contains(g.Id)).Select(g => g.ProjectId).ToArrayAsync(ct);
        conflicts.AddRange(issues.Where(i => !publishedProjects.Contains(i.ProjectId)));
        foreach (var c in candidates.Values.OrderBy(x => x.DepartmentId).ThenBy(x => x.ProjectId))
        {
            if (already.Contains(c.GateId)) continue;
            ReviewGate3Allocation? selected = null;
            for (var day = input.StartDate; day <= input.EndDate && selected is null; day = day.AddDays(1))
            {
                if (!Working(day, calendar.Mask, calendar.Holidays)) continue;
                for (var slot = 1; slot <= input.MaxSlotsPerDay && selected is null; slot++)
                {
                    var time = DefenseSchedulingRules.Slot(day, slot);
                    if (time.Start < c.WindowStart || time.End > c.WindowEnd) continue;
                    foreach (var room in rooms.Values.Where(r => r.Capacity >= c.Seats).OrderBy(r => r.Id))
                    {
                        if (Excluded(room.Id, day, slot, input.Exclusions) || Clash(c, room, time.Start, time.End, occupied)) continue;
                        selected = new(c.ProjectId, c.GateId, room.Id, room.Code, day, slot, time.Start, time.End);
                        occupied.Add(new(c.ProjectId, c.GateId, room.Id, room.Code, time.Start, time.End, c.Reviewers));
                        break;
                    }
                }
            }
            if (selected is null) conflicts.Add(new(c.ProjectId, "NO_ROOM_OR_CONFLICT_FREE_SLOT"));
            else allocations.Add(selected);
        }
        return new(allocations.Count, conflicts.Count, allocations, conflicts, await Revision(ct));
    }

    public async Task<IReadOnlyList<ReviewGate3ScheduleDto>> Apply(long periodId,
        ReviewGate3ScheduleApplyRequest input, CancellationToken ct)
    {
        var department = await Department(periodId, ct);
        if (input.Allocations is null || input.Allocations.Count is < 1 or > 1000
            || input.Allocations.Any(x => x is null)
            || input.Allocations.Select(x => x.ProjectId).Distinct().Count() != input.Allocations.Count)
            throw Invalid("Apply requires 1..1000 unique project allocations.");
        var roomIds = input.Allocations.Select(x => x.RoomId).Distinct().ToArray();
        ValidateExclusions(input.Exclusions, null, null, null);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        if (input.ConcurrencyToken != await Revision(ct))
            throw new ConflictException("Review schedule changed; preview again.", "REVIEW_GATE_3_SCHEDULE_STALE");
        var (candidates, _) = await Candidates(periodId, department, ct);
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => roomIds.Contains(r.Id) && r.Status == "ACTIVE"
            && r.Floor >= 2 && r.Floor <= 4).ToDictionaryAsync(r => r.Id, ct);
        var occupied = await Existing(input.Allocations.Min(x => x.StartAt), input.Allocations.Max(x => x.EndAt), ct);
        var calendar = await Calendar(periodId, ct);
        var gateIds = input.Allocations.Select(x => x.GateInstanceId).ToArray();
        var gates = await db.ProjectGateInstances.Where(g => gateIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        var existingGateIds = (await db.Set<GateSessionScript>().AsNoTracking()
            .Where(s => gateIds.Contains(s.ProjectGateInstanceId))
            .Select(s => s.ProjectGateInstanceId).ToArrayAsync(ct)).ToHashSet();
        var created = new List<(ProjectGateInstance Gate, GateSessionScript Script, Room Room)>();
        foreach (var a in input.Allocations)
        {
            if (!candidates.TryGetValue(a.ProjectId, out var c) || c.GateId != a.GateInstanceId
                || !rooms.TryGetValue(a.RoomId, out var room) || room.Code != a.RoomCode
                || room.Capacity < c.Seats || a.SlotNumber is < 1 or > 6 || a.StartAt <= Now
                || !Working(a.Date, calendar.Mask, calendar.Holidays)
                || Excluded(a.RoomId, a.Date, a.SlotNumber, input.Exclusions))
                throw new ConflictException("Review allocation is ineligible.", "REVIEW_GATE_3_ALLOCATION_INVALID");
            var slot = DefenseSchedulingRules.Slot(a.Date, a.SlotNumber);
            if (a.StartAt != slot.Start || a.EndAt != slot.End || slot.Start < c.WindowStart || slot.End > c.WindowEnd
                || Clash(c, room, slot.Start, slot.End, occupied))
                throw new ConflictException("Review allocation conflicts with the current timetable.", "REVIEW_GATE_3_SCHEDULE_CONFLICT");
            if (existingGateIds.Contains(c.GateId))
                throw new ConflictException("A Gate 3 session already exists; change it through the schedule endpoint.",
                    "REVIEW_GATE_3_ALREADY_SCHEDULED");
            var gate = gates[c.GateId];
            var script = new GateSessionScript { ProjectGateInstanceId = c.GateId, Mode = "OFFLINE",
                RoomId = room.Id, ScheduledStartAt = slot.Start, ScheduledEndAt = slot.End,
                PublishedAt = Now, PublishedBy = Actor, CreatedAt = Now, UpdatedAt = Now };
            db.Add(script); created.Add((gate, script, room));
            occupied.Add(new(c.ProjectId, c.GateId, room.Id, room.Code, slot.Start, slot.End, c.Reviewers));
        }
        await db.SaveChangesAsync(ct);
        foreach (var (gate, script, _) in created)
            await ScheduleNotificationQueue.Gate3(db, Actor, gate.ProjectId, gate.Id, script.Id, Now.UtcDateTime, false, ct);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_3_SCHEDULE_PUBLISHED", "PROJECT_PERIOD", periodId,
            new Dictionary<string, object?> { ["gateIds"] = created.Select(x => x.Gate.Id).ToArray() }), ct);
        await tx.CommitAsync(ct);
        return created.Select(x => Dto(x.Gate, x.Script, x.Room)).ToArray();
    }

    public async Task<IReadOnlyList<ReviewGate3ScheduleDto>> List(long periodId, CancellationToken ct)
    {
        var department = await Department(periodId, ct);
        var projects = db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectPeriodId == periodId
            && (!department.HasValue || s.LeadDepartmentId == department)).Select(s => s.ProjectId);
        var gates = await db.ProjectGateInstances.AsNoTracking().Where(g => projects.Contains(g.ProjectId)
            && g.Sequence == 3).ToArrayAsync(ct);
        var ids = gates.Select(g => g.Id).ToArray();
        var scripts = await db.Set<GateSessionScript>().AsNoTracking().Where(s => ids.Contains(s.ProjectGateInstanceId)
            && s.PublishedAt != null && s.RoomId != null).ToArrayAsync(ct);
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => scripts.Select(s => s.RoomId).Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        return scripts.Where(s => rooms.ContainsKey(s.RoomId!.Value))
            .Select(s => Dto(gates.Single(g => g.Id == s.ProjectGateInstanceId), s, rooms[s.RoomId!.Value]))
            .OrderBy(s => s.StartAt).ThenBy(s => s.RoomCode).ToArray();
    }

    public async Task<ReviewGate3ScheduleDto> Change(long periodId, long gateInstanceId,
        ReviewGate3ScheduleChangeRequest input, CancellationToken ct)
    {
        var department = await Department(periodId, ct);
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Length > 2000 || input.StartAt <= Now
            || input.EndAt <= input.StartAt)
            throw Invalid("A future slot and a bounded change reason are required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        var gate = await db.ProjectGateInstances.SingleOrDefaultAsync(g => g.Id == gateInstanceId && g.Sequence == 3, ct)
            ?? throw new NotFoundException("ProjectGate", gateInstanceId);
        if (!await db.Set<ProjectRegistrationSnapshot>().AnyAsync(s => s.ProjectId == gate.ProjectId
            && s.ProjectPeriodId == periodId && (!department.HasValue || s.LeadDepartmentId == department), ct))
            throw new ForbiddenException();
        var script = await db.Set<GateSessionScript>().SingleOrDefaultAsync(s => s.ProjectGateInstanceId == gate.Id
            && s.PublishedAt != null, ct) ?? throw new NotFoundException("Gate3Schedule", gate.Id);
        if (!Guid.TryParse(input.ConcurrencyToken, out var token) || token != script.ConcurrencyToken)
            throw new ConflictException("Review schedule changed; reload before retry.", "REVIEW_GATE_3_SCHEDULE_STALE");
        if (script.ScheduledStartAt <= Now || script.ActualStartAt.HasValue || script.Verdict != "PENDING")
            throw new ConflictException("A started or decided review cannot be moved.", "REVIEW_GATE_3_SESSION_STARTED");
        var day = DateOnly.FromDateTime(input.StartAt.ToOffset(TimeSpan.FromHours(7)).Date);
        if (!Enumerable.Range(1, 6).Any(number => { var s = DefenseSchedulingRules.Slot(day, number);
            return s.Start == input.StartAt && s.End == input.EndAt; })) throw Invalid("Use a full office slot.");
        var calendar = await Calendar(periodId, ct);
        if (!Working(day, calendar.Mask, calendar.Holidays))
            throw new ConflictException("The date is not a working day.", "REVIEW_GATE_3_NON_WORKING_DAY");
        await OfflineGateEligibility.RequirePreviousGateAndWindow(db, gate.ProjectId, 3, input.StartAt, input.EndAt, ct);
        var room = await db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == input.RoomId
            && r.Status == "ACTIVE" && r.Floor >= 2 && r.Floor <= 4, ct)
            ?? throw new ConflictException("The room is unavailable.", "REVIEW_GATE_3_ROOM_UNAVAILABLE");
        var (candidates, _) = await Candidates(periodId, department, ct);
        if (!candidates.TryGetValue(gate.ProjectId, out var c) || room.Capacity < c.Seats)
            throw new ConflictException("The team, reviewers or capacity are no longer eligible.", "REVIEW_GATE_3_ALLOCATION_INVALID");
        var occupied = await Existing(input.StartAt, input.EndAt, ct);
        occupied.RemoveAll(x => x.GateId == gate.Id);
        if (Clash(c, room, input.StartAt, input.EndAt, occupied))
            throw new ConflictException("Room, group or reviewer has another session.", "REVIEW_GATE_3_SCHEDULE_CONFLICT");
        if (script.RoomId == room.Id && script.ScheduledStartAt == input.StartAt && script.ScheduledEndAt == input.EndAt)
            throw Invalid("The requested schedule must change.");
        script.RoomId = room.Id; script.ScheduledStartAt = input.StartAt; script.ScheduledEndAt = input.EndAt;
        script.UpdatedAt = Now; script.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await ScheduleNotificationQueue.Gate3(db, Actor, gate.ProjectId, gate.Id, script.Id, Now.UtcDateTime, true, ct);
        await audit.RecordAsync(new(Actor, "REVIEW_GATE_3_SCHEDULE_CHANGED", "PROJECT_GATE", gate.Id,
            new Dictionary<string, object?> { ["reason"] = input.Reason.Trim(), ["roomId"] = room.Id,
                ["startAt"] = input.StartAt }), ct);
        await tx.CommitAsync(ct);
        return Dto(gate, script, room);
    }
}
