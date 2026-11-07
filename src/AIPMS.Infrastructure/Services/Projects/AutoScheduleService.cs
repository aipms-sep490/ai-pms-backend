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

internal sealed class AutoScheduleService(AipmsDbContext db, ICurrentUser current, IAuditTrail audit,
    TimeProvider clock) : IAutoScheduleService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Candidate(long ProjectId, long CommitteeId, long? MajorId, int Seats,
        long[] Members, long[] Supervisors);
    private sealed record Occupied(long ProjectId, long? RoomId, string RoomCode, DateTimeOffset Start,
        DateTimeOffset End, long[] Members);
    private static ValidationException Invalid(string text) => new(new Dictionary<string, string[]> { ["autoSchedule"] = [text] });

    private async Task<long> Department(long periodId, CancellationToken ct)
    {
        var organization = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == periodId)
            .Select(p => (long?)p.AcademicSemester.OrganizationId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId, Organization = u.Department == null ? (long?)null : u.Department.OrganizationId,
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") }).SingleOrDefaultAsync(ct)
            ?? throw new ForbiddenException();
        if (!user.Staff || user.Organization != organization || !user.DepartmentId.HasValue) throw new ForbiddenException();
        return user.DepartmentId.Value;
    }

    private async Task<string> Revision(CancellationToken ct)
    {
        var sessions = await db.Set<DefenseSession>().AsNoTracking().OrderBy(s => s.Id)
            .Select(s => new { s.Id, s.ConcurrencyToken }).ToArrayAsync(ct);
        var rooms = await db.Set<Room>().AsNoTracking().OrderBy(r => r.Id)
            .Select(r => new { r.Id, r.ConcurrencyToken }).ToArrayAsync(ct);
        var text = string.Join('|', sessions.Select(s => $"s{s.Id}:{s.ConcurrencyToken:N}")) + "/"
            + string.Join('|', rooms.Select(r => $"r{r.Id}:{r.ConcurrencyToken:N}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private async Task<(Dictionary<long, Candidate> Candidates, Dictionary<long, Room> Rooms, long[] MissingCommittees)> Load(long periodId,
        long departmentId, IReadOnlyList<long> roomIds, CancellationToken ct)
    {
        var snapshots = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking()
            .Where(s => s.ProjectPeriodId == periodId && s.LeadDepartmentId == departmentId)
            .OrderByDescending(s => s.Id).ToArrayAsync(ct);
        var projectIds = snapshots.Select(s => s.ProjectId).Distinct().ToArray();
        var projects = await db.Projects.AsNoTracking().Include(p => p.Team).ThenInclude(t => t.TeamMembers)
            .Where(p => projectIds.Contains(p.Id) && (p.Status == "ACTIVE" || p.Status == "FINAL_SUBMISSION"))
            .ToArrayAsync(ct);
        var committees = await db.Set<DefenseCommittee>().AsNoTracking().Include(c => c.Members)
            .Where(c => projectIds.Contains(c.ProjectId)).OrderBy(c => c.Id).ToArrayAsync(ct);
        var supervisors = await db.SupervisorAssignments.AsNoTracking()
            .Where(a => projectIds.Contains(a.ProjectId) && a.EndedAt == null)
            .Select(a => new { a.ProjectId, a.SupervisorProfile.UserId }).ToArrayAsync(ct);
        var majors = await db.ProjectMajors.AsNoTracking().Where(m => projectIds.Contains(m.ProjectId))
            .Select(m => new { m.ProjectId, m.MajorId }).ToArrayAsync(ct);
        var candidates = new Dictionary<long, Candidate>();
        foreach (var project in projects)
        {
            var committee = committees.FirstOrDefault(c => c.ProjectId == project.Id);
            if (committee is null) continue;
            var members = committee.Members.Select(m => m.UserId).Distinct().ToArray();
            candidates.Add(project.Id, new(project.Id, committee.Id,
                majors.FirstOrDefault(m => m.ProjectId == project.Id)?.MajorId,
                project.Team.TeamMembers.Count(m => m.LeftAt == null) + members.Length + 5,
                members, supervisors.Where(s => s.ProjectId == project.Id).Select(s => s.UserId).Distinct().ToArray()));
        }
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => roomIds.Contains(r.Id) && r.Status == "ACTIVE")
            .ToDictionaryAsync(r => r.Id, ct);
        return (candidates, rooms, projects.Where(p => !candidates.ContainsKey(p.Id)).Select(p => p.Id).ToArray());
    }

    private async Task<List<Occupied>> Existing(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var start = from.UtcDateTime; var end = to.UtcDateTime;
        var sessions = await db.Set<DefenseSession>().AsNoTracking().Where(s => s.Status == "SCHEDULED"
            && s.StartAt < end && s.EndAt > start).ToArrayAsync(ct);
        var committeeIds = sessions.Select(s => s.CommitteeId).Distinct().ToArray();
        var members = await db.Set<DefenseCommitteeMember>().AsNoTracking()
            .Where(m => committeeIds.Contains(m.CommitteeId)).ToArrayAsync(ct);
        return sessions.Select(s => new Occupied(s.ProjectId, s.RoomId, s.Room,
            new DateTimeOffset(DateTime.SpecifyKind(s.StartAt, DateTimeKind.Utc)),
            new DateTimeOffset(DateTime.SpecifyKind(s.EndAt, DateTimeKind.Utc)),
            members.Where(m => m.CommitteeId == s.CommitteeId).Select(m => m.UserId).Distinct().ToArray())).ToList();
    }
    private static bool Clash(long projectId, Room room, DateTimeOffset start, DateTimeOffset end,
        IReadOnlyList<long> members, IReadOnlyList<Occupied> occupied) => occupied.Any(s =>
            DefenseSchedulingRules.Overlaps(start, end, s.Start, s.End)
            && (s.ProjectId == projectId || s.RoomId == room.Id || s.RoomId is null && s.RoomCode == room.Code
                || s.Members.Intersect(members).Any()));

    private async Task<(int Mask, PeriodHoliday[] Holidays)> Calendar(long periodId, CancellationToken ct)
    {
        var row = await db.Set<ProjectPeriodCalendar>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectPeriodId == periodId, ct);
        return row is null ? (62, []) : (row.WorkingDaysOfWeek, JsonSerializer.Deserialize<PeriodHoliday[]>(row.HolidaysJson) ?? []);
    }
    private static bool WorkDate(DateOnly date, int mask, IReadOnlyList<PeriodHoliday> holidays) =>
        date.DayOfWeek != DayOfWeek.Sunday && WorkingDayMath.IsWorkingDay(date, mask, holidays);

    public async Task<AutoSchedulePreviewDto> Preview(long periodId, AutoSchedulePreviewRequest input, CancellationToken ct)
    {
        var departmentId = await Department(periodId, ct);
        if (input.StartDate > input.EndDate || input.EndDate.DayNumber - input.StartDate.DayNumber > 31
            || input.RoomIds is null || input.RoomIds.Count is < 1 or > 100 || input.RoomIds.Distinct().Count() != input.RoomIds.Count
            || input.RoomIds.Any(id => id <= 0) || input.MaxSlotsPerDay is < 1 or > 6)
            throw Invalid("Use a date range up to 32 days, 1..100 unique rooms and 1..6 slots per day.");
        var earliest = DefenseSchedulingRules.Slot(input.StartDate, 1).Start;
        if (earliest.UtcDateTime <= Now || input.EndDate.Year > 2200) throw Invalid("Defense dates must be in the future.");
        var (candidates, rooms, missingCommittees) = await Load(periodId, departmentId, input.RoomIds, ct);
        if (rooms.Count != input.RoomIds.Count) throw new ConflictException("Selected rooms must be active and available.", "DEFENSE_ROOM_UNAVAILABLE");
        var occupied = await Existing(earliest, DefenseSchedulingRules.Slot(input.EndDate, 6).End, ct);
        var calendar = await Calendar(periodId, ct);
        var allocations = new List<DefenseAllocation>(); var conflicts = new List<DefenseScheduleConflict>();
        var existingProjects = (await db.Set<DefenseSession>().AsNoTracking().Where(s => s.Status == "SCHEDULED")
            .Select(s => s.ProjectId).Distinct().ToArrayAsync(ct)).ToHashSet();
        conflicts.AddRange(missingCommittees.Where(id => !existingProjects.Contains(id))
            .Select(id => new DefenseScheduleConflict(id, "DEFENSE_COMMITTEE_REQUIRED")));
        foreach (var candidate in candidates.Values.OrderBy(c => c.MajorId).ThenBy(c => c.ProjectId))
        {
            if (existingProjects.Contains(candidate.ProjectId)) continue;
            if (candidate.Members.Length == 0 || candidate.Members.Intersect(candidate.Supervisors).Any())
            { conflicts.Add(new(candidate.ProjectId, "SUPERVISOR_CONFLICT_OF_INTEREST")); continue; }
            DefenseAllocation? chosen = null;
            for (var day = input.StartDate; day <= input.EndDate && chosen is null; day = day.AddDays(1))
            {
                if (!WorkDate(day, calendar.Mask, calendar.Holidays)) continue;
                for (var slot = 1; slot <= input.MaxSlotsPerDay && chosen is null; slot++)
                {
                    var times = DefenseSchedulingRules.Slot(day, slot);
                    foreach (var room in rooms.Values.Where(r => r.Capacity >= candidate.Seats)
                        .OrderByDescending(r => allocations.Count(a => a.RoomId == r.Id && candidates[a.ProjectId].MajorId == candidate.MajorId))
                        .ThenBy(r => r.Id))
                    {
                        if (Clash(candidate.ProjectId, room, times.Start, times.End, candidate.Members, occupied)) continue;
                        chosen = new(candidate.ProjectId, candidate.CommitteeId, room.Id, room.Code, day, slot, times.Start, times.End);
                        occupied.Add(new(candidate.ProjectId, room.Id, room.Code, times.Start, times.End, candidate.Members));
                        break;
                    }
                }
            }
            if (chosen is null) conflicts.Add(new(candidate.ProjectId, "NO_ROOM_OR_CONFLICT_FREE_SLOT"));
            else allocations.Add(chosen);
        }
        return new(allocations.Count, conflicts.Count, allocations, conflicts, await Revision(ct));
    }

    public async Task<AutoScheduleApplyDto> Apply(long periodId, AutoScheduleApplyRequest input, CancellationToken ct)
    {
        var departmentId = await Department(periodId, ct);
        if (input.Allocations is null || input.Allocations.Count is < 1 or > 1000
            || input.Allocations.Any(a => a is null)
            || input.Allocations.Select(a => a.ProjectId).Distinct().Count() != input.Allocations.Count)
            throw Invalid("Apply requires 1..1000 allocations with unique projects.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockResult = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (lockResult.Single() < 0) throw new ConflictException("Defense schedule is busy; retry.", "DEFENSE_SCHEDULE_BUSY");
        if (input.ConcurrencyToken != await Revision(ct)) throw new ConflictException("Defense schedule changed; preview again.", "DEFENSE_SCHEDULE_STALE");
        var roomIds = input.Allocations.Select(a => a.RoomId).Distinct().ToArray();
        var (candidates, rooms, _) = await Load(periodId, departmentId, roomIds, ct);
        if (rooms.Count != roomIds.Length) throw new ConflictException("A selected room is unavailable.", "DEFENSE_ROOM_UNAVAILABLE");
        var calendar = await Calendar(periodId, ct);
        var from = input.Allocations.Min(a => a.StartAt); var to = input.Allocations.Max(a => a.EndAt);
        var occupied = await Existing(from, to, ct);
        var projectIds = input.Allocations.Select(a => a.ProjectId).ToArray();
        if (await db.Set<DefenseSession>().AnyAsync(s => projectIds.Contains(s.ProjectId) && s.Status == "SCHEDULED", ct))
            throw new ConflictException("A project already has a scheduled defense.", "DEFENSE_ALREADY_SCHEDULED");
        foreach (var allocation in input.Allocations)
        {
            if (!candidates.TryGetValue(allocation.ProjectId, out var candidate) || candidate.CommitteeId != allocation.CommitteeId
                || !rooms.TryGetValue(allocation.RoomId, out var room) || room.Code != allocation.RoomCode
                || room.Capacity < candidate.Seats || candidate.Members.Length == 0 || candidate.Members.Intersect(candidate.Supervisors).Any()
                || allocation.SlotNumber is < 1 or > 6
                || allocation.StartAt.UtcDateTime <= Now || !WorkDate(allocation.Date, calendar.Mask, calendar.Holidays))
                throw new ConflictException("Allocation is no longer eligible.", "DEFENSE_ALLOCATION_INVALID");
            var times = DefenseSchedulingRules.Slot(allocation.Date, allocation.SlotNumber);
            if (allocation.StartAt != times.Start || allocation.EndAt != times.End
                || Clash(candidate.ProjectId, room, times.Start, times.End, candidate.Members, occupied))
                throw new ConflictException("Allocation conflicts with the current defense timetable.", "DEFENSE_SCHEDULE_CONFLICT");
            occupied.Add(new(candidate.ProjectId, room.Id, room.Code, times.Start, times.End, candidate.Members));
            db.Add(new DefenseSession { ProjectId = candidate.ProjectId, CommitteeId = candidate.CommitteeId,
                RoomId = room.Id, Room = room.Code, StartAt = times.Start.UtcDateTime, EndAt = times.End.UtcDateTime,
                CreatedBy = Actor, CreatedAt = Now });
        }
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "DEFENSE_AUTO_SCHEDULE_APPLIED", "PROJECT_PERIOD", periodId,
            new Dictionary<string, object?> { ["count"] = input.Allocations.Count,
                ["projects"] = input.Allocations.Select(a => a.ProjectId).ToArray() }), ct);
        await tx.CommitAsync(ct);
        return new(true, input.Allocations.Count);
    }
}
