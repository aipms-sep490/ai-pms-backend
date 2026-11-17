using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.AcademicTracking;
using AIPMS.Application.Features.AcademicTracking.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class DefenseCatalogService(AipmsDbContext db, ICurrentUser current, IAuditTrail audit,
    TimeProvider clock) : IDefenseCatalogService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static RoomDto Dto(Room room) => new(room.Id, room.Code, room.Building, room.Floor, room.Capacity,
        room.Status, room.ConcurrencyToken.ToString("N"));
    private static DateTimeOffset Local(DateTime value) => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToOffset(VietnamOffset);

    private async Task<(bool Admin, bool Staff, long? OrganizationId, long? DepartmentId)> User(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { u.DepartmentId,
                OrganizationId = u.Department == null ? (long?)null : u.Department.OrganizationId,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF" || r.Role.Code == "DEPARTMENT_HEAD") })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        return (user.Admin, user.Staff, user.OrganizationId, user.DepartmentId);
    }
    private async Task Staff(CancellationToken ct)
    {
        var user = await User(ct);
        if (!user.Admin && !user.Staff) throw new ForbiddenException("Room management requires staff authority.");
    }
    private async Task<(bool Admin, bool Staff, long? DepartmentId)> PeriodStaff(long periodId, CancellationToken ct)
    {
        var organizationId = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == periodId)
            .Select(p => (long?)p.AcademicSemester.OrganizationId).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", periodId);
        var user = await User(ct);
        if (!user.Admin && (!user.Staff || user.OrganizationId != organizationId))
            throw new ForbiddenException("The project period is outside the actor's staff scope.");
        return (user.Admin, user.Staff, user.DepartmentId);
    }
    private static void Validate(SaveRoomRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Trim().Length > 50
            || string.IsNullOrWhiteSpace(input.Building) || input.Building.Trim().Length > 50
            || input.Floor is < 0 or > 100 || input.Capacity is < 1 or > 10000
            || input.Status is not ("ACTIVE" or "MAINTENANCE" or "INACTIVE"))
            throw new ValidationException(new Dictionary<string, string[]> { ["room"] = ["A valid code, building, floor, capacity and status are required."] });
    }
    private static void Token(Guid actual, string? input)
    {
        if (!Guid.TryParse(input, out var value) || value != actual)
            throw new ConflictException("The room changed; reload before retrying.", "ROOM_STALE");
    }
    private async Task ScheduleLock(CancellationToken ct)
    {
        var result = await db.Database.SqlQueryRaw<int>("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'AI_PMS_DEFENSE_SCHEDULE', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; SELECT @r AS Value;").ToListAsync(ct);
        if (result.Single() < 0) throw new ConflictException("The room catalog is busy; retry.", "ROOM_CATALOG_BUSY");
    }
    public async Task<IReadOnlyList<RoomDto>> Rooms(string? building, string? status, int? minCapacity, CancellationToken ct)
    {
        await User(ct);
        if (minCapacity is < 1 || status is not null and not ("ACTIVE" or "MAINTENANCE" or "INACTIVE"))
            throw new ValidationException(new Dictionary<string, string[]> { ["room"] = ["Invalid room filter."] });
        var query = db.Set<Room>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(building)) query = query.Where(r => r.Building == building.Trim());
        if (status is not null) query = query.Where(r => r.Status == status);
        if (minCapacity.HasValue) query = query.Where(r => r.Capacity >= minCapacity);
        return (await query.OrderBy(r => r.Building).ThenBy(r => r.Floor).ThenBy(r => r.Code).ToArrayAsync(ct)).Select(Dto).ToArray();
    }
    public async Task<RoomDto> CreateRoom(SaveRoomRequest input, CancellationToken ct)
    {
        await Staff(ct); Validate(input);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await ScheduleLock(ct);
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.Set<Room>().AnyAsync(r => r.Code == code, ct)) throw new ConflictException("Room code already exists.", "ROOM_CODE_EXISTS");
        var row = new Room { Code = code, Building = input.Building.Trim(), Floor = input.Floor,
            Capacity = input.Capacity, Status = input.Status, CreatedAt = Now, UpdatedAt = Now };
        db.Add(row); await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "ROOM_CREATED", "ROOM", row.Id,
            new Dictionary<string, object?> { ["code"] = code }), ct);
        await tx.CommitAsync(ct);
        return Dto(row);
    }
    public async Task<RoomDto> UpdateRoom(long id, SaveRoomRequest input, CancellationToken ct)
    {
        await Staff(ct); Validate(input);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await ScheduleLock(ct);
        var row = await db.Set<Room>().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Room", id);
        Token(row.ConcurrencyToken, input.ConcurrencyToken);
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.Set<Room>().AnyAsync(r => r.Code == code && r.Id != id, ct)) throw new ConflictException("Room code already exists.", "ROOM_CODE_EXISTS");
        if (code != row.Code && await db.Set<DefenseSession>().AnyAsync(s => s.RoomId == id, ct))
            throw new ConflictException("A room with defense history cannot be renamed.", "ROOM_HAS_HISTORY");
        if (input.Status != "ACTIVE" && await db.Set<DefenseSession>().AnyAsync(s => s.RoomId == id && s.Status == "SCHEDULED" && s.EndAt > Now, ct))
            throw new ConflictException("Future defenses must be moved before deactivating this room.", "ROOM_HAS_FUTURE_SESSIONS");
        row.Code = code; row.Building = input.Building.Trim(); row.Floor = input.Floor;
        row.Capacity = input.Capacity; row.Status = input.Status; row.UpdatedAt = Now; row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "ROOM_UPDATED", "ROOM", id,
            new Dictionary<string, object?> { ["status"] = row.Status, ["code"] = code }), ct);
        await tx.CommitAsync(ct);
        return Dto(row);
    }
    public async Task<RoomDto> DeactivateRoom(long id, string concurrencyToken, CancellationToken ct)
    {
        await Staff(ct);
        var row = await db.Set<Room>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Room", id);
        return await UpdateRoom(id, new(row.Code, row.Building, row.Floor, row.Capacity, "INACTIVE", concurrencyToken), ct);
    }
    public async Task<IReadOnlyList<PeriodDefenseSessionDto>> PeriodSessions(long periodId, DateOnly? date, CancellationToken ct)
    {
        var actor = await PeriodStaff(periodId, ct);
        var projects = db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => s.ProjectPeriodId == periodId).Select(s => s.ProjectId).Distinct();
        var query = db.Set<DefenseSession>().AsNoTracking().Where(s => projects.Contains(s.ProjectId));
        if (date.HasValue)
        {
            var start = new DateTimeOffset(date.Value.ToDateTime(TimeOnly.MinValue), VietnamOffset).UtcDateTime;
            var end = start.AddDays(1);
            query = query.Where(s => s.StartAt < end && s.EndAt > start);
        }
        var rows = await query.OrderBy(s => s.StartAt).ThenBy(s => s.Id).ToArrayAsync(ct);
        var ids = rows.Select(r => r.ProjectId).Distinct().ToArray();
        var projectDetails = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Title }).ToDictionaryAsync(p => p.Id, ct);
        var committeeIds = rows.Select(r => r.CommitteeId).Distinct().ToArray();
        var committees = await db.Set<DefenseCommittee>().AsNoTracking().Where(c => committeeIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, ct);
        var roomIds = rows.Where(r => r.RoomId.HasValue).Select(r => r.RoomId!.Value).Distinct().ToArray();
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var scopes = await db.Set<ProjectRegistrationSnapshot>().AsNoTracking().Where(s => ids.Contains(s.ProjectId))
            .Select(s => new { s.ProjectId, s.LeadDepartmentId, s.Id }).ToArrayAsync(ct);
        var leads = scopes.GroupBy(s => s.ProjectId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Id).First().LeadDepartmentId);
        return rows.Select(s => {
            var local = Local(s.StartAt);
            var slot = new[] { new TimeOnly(8, 0), new TimeOnly(9, 30), new TimeOnly(11, 0),
                new TimeOnly(13, 30), new TimeOnly(15, 0), new TimeOnly(16, 30) }.ToList().IndexOf(TimeOnly.FromDateTime(local.DateTime));
            return new PeriodDefenseSessionDto(s.Id, s.ProjectId, projectDetails[s.ProjectId].Code,
                projectDetails[s.ProjectId].Title, s.CommitteeId, committees[s.CommitteeId].Name,
                s.RoomId, s.RoomId.HasValue && rooms.TryGetValue(s.RoomId.Value, out var room) ? room.Code : s.Room,
                slot < 0 ? null : slot + 1, local, Local(s.EndAt), s.Status, s.ConcurrencyToken.ToString("N"),
                actor.Staff && actor.DepartmentId == leads.GetValueOrDefault(s.ProjectId)
                    && s.Status == "SCHEDULED" && s.StartAt > Now);
        }).ToArray();
    }
    public async Task<IReadOnlyList<RoomDto>> AvailableRooms(long periodId, DateOnly date, TimeOnly startAt, TimeOnly endAt, CancellationToken ct)
    {
        await PeriodStaff(periodId, ct);
        if (endAt <= startAt) throw new ValidationException(new Dictionary<string, string[]> { ["time"] = ["End time must follow start time."] });
        var start = new DateTimeOffset(date.ToDateTime(startAt), VietnamOffset).UtcDateTime;
        var end = new DateTimeOffset(date.ToDateTime(endAt), VietnamOffset).UtcDateTime;
        var occupied = db.Set<DefenseSession>().AsNoTracking().Where(s => s.Status == "SCHEDULED" && s.StartAt < end && s.EndAt > start);
        var gateOccupied = db.Set<GateSessionScript>().AsNoTracking().Where(s => s.Mode == "OFFLINE"
            && s.ScheduledStartAt < new DateTimeOffset(end, TimeSpan.Zero)
            && s.ScheduledEndAt > new DateTimeOffset(start, TimeSpan.Zero));
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => r.Status == "ACTIVE"
            && !occupied.Any(s => s.RoomId == r.Id || s.RoomId == null && s.Room == r.Code)
            && !gateOccupied.Any(s => s.RoomId == r.Id))
            .OrderBy(r => r.Building).ThenBy(r => r.Code).ToArrayAsync(ct);
        return rooms.Select(Dto).ToArray();
    }
}
