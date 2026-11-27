using AIPMS.Application.Features.Semesters;

namespace AIPMS.Application.Features.Semesters.Abstractions;

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
    DateTimeOffset AddElapsedWorkingDays(DateTimeOffset from, int workingDays, long projectPeriodId) =>
        WorkingDayMath.AddElapsedWorkingDays(from, workingDays, 62, []);
    int CountElapsedWorkingDays(DateTimeOffset from, DateTimeOffset to, long projectPeriodId) =>
        WorkingDayMath.CountElapsedWorkingDays(from, to, 62, []);
}
