namespace AIPMS.Application.Features.Semesters;

public sealed record PeriodHoliday(DateOnly Date, string Name, bool IsRecurring);
public sealed record PeriodCalendarDto(long ProjectPeriodId, int WorkingDaysOfWeek,
    string WorkingDaysDescription, IReadOnlyList<PeriodHoliday> Holidays, string ConcurrencyToken);
public sealed record SavePeriodCalendarRequest(int WorkingDaysOfWeek, IReadOnlyList<PeriodHoliday> Holidays, string ConcurrencyToken);
public sealed record LoadVietnamHolidaysRequest(int Year, string ConcurrencyToken);
