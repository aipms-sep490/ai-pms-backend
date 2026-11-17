using System.Threading.Tasks;
using System.Text.Json;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Semesters;
using AIPMS.Application.Features.Semesters.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class PeriodCalendarService(AipmsDbContext db, ICurrentUser current, IAuditTrail audit,
    TimeProvider clock) : IPeriodCalendarService
{
    private long Actor => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new UnauthorizedException();
    private static List<PeriodHoliday> Holidays(ProjectPeriodCalendar? row) => row is null ? []
        : JsonSerializer.Deserialize<List<PeriodHoliday>>(row.HolidaysJson) ?? [];
    private static PeriodCalendarDto Dto(long id, ProjectPeriodCalendar? row) => new(id,
        row?.WorkingDaysOfWeek ?? 62, (row?.WorkingDaysOfWeek ?? 62) == 62 ? "Thứ 2 đến Thứ 6"
            : row?.WorkingDaysOfWeek == 126 ? "Thứ 2 đến Thứ 7" : "Ngày làm việc đã cấu hình",
        Holidays(row).OrderBy(h => h.Date).ToArray(), (row?.ConcurrencyToken ?? Guid.Empty).ToString("N"));

    private async Task Authorize(long id, bool write, CancellationToken ct)
    {
        var period = await db.ProjectPeriods.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new { p.Id, OrganizationId = p.AcademicSemester.OrganizationId }).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("ProjectPeriod", id);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == Actor && u.Status == "ACTIVE")
            .Select(u => new { Org = u.Department == null ? (long?)null : u.Department.OrganizationId,
                ActiveDepartment = u.Department != null && u.Department.IsActive && u.Department.Organization.IsActive,
                Admin = u.UserRoleUsers.Any(r => r.Role.Code == "ADMIN"),
                Staff = u.UserRoleUsers.Any(r => r.Role.Code == "DEPARTMENT_STAFF") })
            .SingleOrDefaultAsync(ct) ?? throw new ForbiddenException();
        if (!user.Admin && !user.ActiveDepartment) throw new ForbiddenException();
        if (write && !(user.Admin || user.Staff && user.Org == period.OrganizationId))
            throw new ForbiddenException("Academic period calendar management requires authorized staff.");
        if (!write && !user.Admin && user.Org != period.OrganizationId) throw new ForbiddenException();
    }

    public async Task<PeriodCalendarDto> Get(long projectPeriodId, CancellationToken ct)
    {
        await Authorize(projectPeriodId, false, ct);
        var row = await db.Set<ProjectPeriodCalendar>().AsNoTracking().SingleOrDefaultAsync(c => c.ProjectPeriodId == projectPeriodId, ct);
        return Dto(projectPeriodId, row);
    }

    public async Task<PeriodCalendarDto> Save(long projectPeriodId, SavePeriodCalendarRequest input, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Authorize(projectPeriodId, true, ct);
        if (input.WorkingDaysOfWeek is < 1 or > 127 || input.Holidays is null || input.Holidays.Count > 500
            || input.Holidays.Any(h => h is null || string.IsNullOrWhiteSpace(h.Name) || h.Name.Length > 200)
            || input.Holidays.Select(h => h.Date).Distinct().Count() != input.Holidays.Count)
            throw new ValidationException(new Dictionary<string, string[]> { ["calendar"] = ["A valid working-day mask and at most 500 distinct named holidays are required."] });
        // Serialize first-time creation per period; token comparisons protect subsequent edits.
        await db.ProjectPeriods.FromSqlInterpolated($"SELECT * FROM dbo.project_periods WITH (UPDLOCK,HOLDLOCK) WHERE id={projectPeriodId}")
            .AsNoTracking().SingleAsync(ct);
        var row = await db.Set<ProjectPeriodCalendar>().SingleOrDefaultAsync(c => c.ProjectPeriodId == projectPeriodId, ct);
        var expected = row?.ConcurrencyToken ?? Guid.Empty;
        if (!Guid.TryParse(input.ConcurrencyToken, out var received) || received != expected)
            throw new ConflictException("The period calendar changed; reload before retrying.", "PERIOD_CALENDAR_STALE");
        var now = clock.GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new ProjectPeriodCalendar { ProjectPeriodId = projectPeriodId, CreatedAt = now };
            db.Add(row);
        }
        row.WorkingDaysOfWeek = input.WorkingDaysOfWeek;
        row.HolidaysJson = JsonSerializer.Serialize(input.Holidays.OrderBy(h => h.Date));
        row.UpdatedAt = now;
        row.ConcurrencyToken = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(new(Actor, "PERIOD_CALENDAR_UPDATED", "PROJECT_PERIOD_CALENDAR", row.Id,
            new Dictionary<string, object?> { ["projectPeriodId"] = projectPeriodId, ["holidayCount"] = input.Holidays.Count }), ct);
        await tx.CommitAsync(ct);
        return Dto(projectPeriodId, row);
    }

    public async Task<PeriodCalendarDto> LoadVietnamHolidays(long projectPeriodId, LoadVietnamHolidaysRequest input, CancellationToken ct)
    {
        await Authorize(projectPeriodId, true, ct);
        if (input.Year is < 2025 or > 2030) throw new ValidationException(new Dictionary<string, string[]> { ["year"] = ["Supported years: 2025 through 2030."] });
        var currentCalendar = await Get(projectPeriodId, ct);
        if (!Guid.TryParse(input.ConcurrencyToken, out var token) || token.ToString("N") != currentCalendar.ConcurrencyToken)
            throw new ConflictException("The period calendar changed; reload before retrying.", "PERIOD_CALENDAR_STALE");
        var existing = currentCalendar.Holidays.ToDictionary(h => h.Date);
        foreach (var holiday in VietnamHolidays.ForYear(input.Year)) existing.TryAdd(holiday.Date, holiday);
        return await Save(projectPeriodId, new(currentCalendar.WorkingDaysOfWeek, existing.Values.ToArray(), input.ConcurrencyToken), ct);
    }
}

internal sealed class WorkingDayCalculator(AipmsDbContext db, ILogger<WorkingDayCalculator> logger) : IWorkingDayCalculator
{
    private (int Mask, IReadOnlyList<PeriodHoliday> Holidays)? Calendar(long periodId)
    {
        var row = db.Set<ProjectPeriodCalendar>().AsNoTracking().SingleOrDefault(c => c.ProjectPeriodId == periodId);
        if (row is null) { logger.LogWarning("No working calendar configured for project period {ProjectPeriodId}; using calendar time", periodId); return null; }
        return (row.WorkingDaysOfWeek, JsonSerializer.Deserialize<PeriodHoliday[]>(row.HolidaysJson) ?? []);
    }
    public DateTimeOffset AddWorkingDays(DateTimeOffset from, int workingDays, long projectPeriodId)
    {
        var calendar = Calendar(projectPeriodId);
        return calendar is null ? from.AddDays(workingDays) : WorkingDayMath.AddWorkingDays(from, workingDays, calendar.Value.Mask, calendar.Value.Holidays);
    }
    public DateTimeOffset AddWorkingHours(DateTimeOffset from, int workingHours, long projectPeriodId)
    {
        var calendar = Calendar(projectPeriodId);
        return calendar is null ? from.AddHours(workingHours) : WorkingDayMath.AddWorkingHours(from, workingHours, calendar.Value.Mask, calendar.Value.Holidays);
    }
}

internal static class VietnamHolidays
{
    private static readonly IReadOnlyDictionary<int, (DateOnly Tet, DateOnly Hung)> Dates = new Dictionary<int, (DateOnly, DateOnly)> {
        [2025] = (new(2025, 1, 29), new(2025, 4, 7)), [2026] = (new(2026, 2, 17), new(2026, 4, 26)),
        [2027] = (new(2027, 2, 6), new(2027, 4, 16)), [2028] = (new(2028, 1, 26), new(2028, 4, 4)),
        [2029] = (new(2029, 2, 13), new(2029, 4, 23)), [2030] = (new(2030, 2, 3), new(2030, 4, 12)) };
    public static IReadOnlyList<PeriodHoliday> ForYear(int year)
    {
        var dates = Dates[year];
        var holidays = new List<PeriodHoliday> {
            new(new(year, 1, 1), "Tết Dương lịch", true),
            new(dates.Hung, "Giỗ Tổ Hùng Vương", false),
            new(new(year, 4, 30), "Giải phóng miền Nam", true),
            new(new(year, 5, 1), "Quốc tế Lao động", true),
            new(new(year, 9, 2), "Quốc khánh", true) };
        // Baseline lunar dates; the academic calendar editor records the actual annual Tet/compensatory schedule.
        for (var day = -1; day <= 3; day++) holidays.Add(new(dates.Tet.AddDays(day), "Tết Nguyên Đán", false));
        if (dates.Hung.DayOfWeek == DayOfWeek.Sunday) holidays.Add(new(dates.Hung.AddDays(1), "Nghỉ bù Giỗ Tổ Hùng Vương", false));
        return holidays;
    }
}
