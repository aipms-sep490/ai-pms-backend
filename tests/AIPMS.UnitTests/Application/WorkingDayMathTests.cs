using AIPMS.Application.Features.Semesters;

namespace AIPMS.UnitTests.Application;

public sealed class WorkingDayMathTests
{
    [Fact]
    public void Five_working_dates_from_april_28_end_may_6_across_holidays_and_weekend()
    {
        PeriodHoliday[] holidays = [new(new DateOnly(2026, 4, 30), "Liberation Day", true),
            new(new DateOnly(2026, 5, 1), "Labour Day", true)];
        var from = new DateTimeOffset(2026, 4, 28, 16, 0, 0, TimeSpan.FromHours(7));
        Assert.Equal(new DateTimeOffset(2026, 5, 6, 16, 0, 0, TimeSpan.FromHours(7)),
            WorkingDayMath.AddWorkingDays(from, 5, 62, holidays));
        Assert.False(WorkingDayMath.IsWorkingDay(new DateOnly(2027, 4, 30), 62, holidays));
    }

    [Fact]
    public void Working_hours_skip_lunch_weekend_and_holiday()
    {
        PeriodHoliday[] holidays = [new(new DateOnly(2026, 5, 1), "Labour Day", true)];
        var from = new DateTimeOffset(2026, 4, 30, 11, 0, 0, TimeSpan.FromHours(7));
        Assert.Equal(new DateTimeOffset(2026, 5, 4, 14, 30, 0, TimeSpan.FromHours(7)),
            WorkingDayMath.AddWorkingHours(from, 10, 62, holidays));
    }

    [Fact]
    public void ParseHolidaysJson_SupportsStructuredObjectsAndLegacyDateStringArrays()
    {
        var structured = AIPMS.Infrastructure.Services.Projects.PeriodCalendarService.ParseHolidaysJson(
            """[{"Date":"2026-09-02","Name":"Quốc khánh 2/9","IsRecurring":true}]""");
        var single = Assert.Single(structured);
        Assert.Equal(new DateOnly(2026, 9, 2), single.Date);
        Assert.True(single.IsRecurring);

        var legacy = AIPMS.Infrastructure.Services.Projects.PeriodCalendarService.ParseHolidaysJson("""["2026-09-02"]""");
        var legacySingle = Assert.Single(legacy);
        Assert.Equal(new DateOnly(2026, 9, 2), legacySingle.Date);
    }
}
