namespace AIPMS.Application.Features.Semesters;

public sealed record PeriodHoliday(DateOnly Date, string Name, bool IsRecurring);
public sealed record PeriodCalendarDto(long ProjectPeriodId, int WorkingDaysOfWeek,
    string WorkingDaysDescription, IReadOnlyList<PeriodHoliday> Holidays, string ConcurrencyToken);
public sealed record SavePeriodCalendarRequest(int WorkingDaysOfWeek, IReadOnlyList<PeriodHoliday> Holidays, string ConcurrencyToken);
public sealed record LoadVietnamHolidaysRequest(int Year, string ConcurrencyToken);

public interface IPeriodCalendarService
{
    Task<PeriodCalendarDto> Get(long projectPeriodId, CancellationToken ct);
    Task<PeriodCalendarDto> Save(long projectPeriodId, SavePeriodCalendarRequest input, CancellationToken ct);
    Task<PeriodCalendarDto> LoadVietnamHolidays(long projectPeriodId, LoadVietnamHolidaysRequest input, CancellationToken ct);
}

public interface IWorkingDayCalculator
{
    DateTimeOffset AddWorkingDays(DateTimeOffset from, int workingDays, long projectPeriodId);
    DateTimeOffset AddWorkingHours(DateTimeOffset from, int workingHours, long projectPeriodId);
}
