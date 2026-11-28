namespace AIPMS.Application.Features.Semesters;

public static class WorkingDayMath
{
    public static bool IsWorkingDay(DateOnly date, int mask, IReadOnlyList<PeriodHoliday> holidays) =>
        (mask & (1 << (int)date.DayOfWeek)) != 0 && !holidays.Any(h => h.Date == date || h.IsRecurring && h.Date.Month == date.Month && h.Date.Day == date.Day);

    // The starting working date counts as day one, matching the period's remediation deadline convention.
    public static DateTimeOffset AddWorkingDays(DateTimeOffset from, int days, int mask, IReadOnlyList<PeriodHoliday> holidays)
    {
        if (days < 0 || days > 3660) throw new ArgumentOutOfRangeException(nameof(days));
        var result = from;
        for (var counted = 0; counted < days; result = result.AddDays(1))
        {
            if (IsWorkingDay(DateOnly.FromDateTime(result.Date), mask, holidays)) counted++;
            if (counted == days) return result;
        }
        return result;
    }

    // Strictly elapsed working days after 'from' (Mon 10:00 + 2 WD = Wed 10:00; Fri 10:00 + 2 WD = Tue 10:00).
    public static DateTimeOffset AddElapsedWorkingDays(DateTimeOffset from, int days, int mask, IReadOnlyList<PeriodHoliday> holidays)
    {
        if (days < 0 || days > 3660) throw new ArgumentOutOfRangeException(nameof(days));
        if (days == 0) return from;
        var result = from;
        var counted = 0;
        while (counted < days)
        {
            result = result.AddDays(1);
            if (IsWorkingDay(DateOnly.FromDateTime(result.Date), mask, holidays))
                counted++;
        }
        return result;
    }

    public static int CountElapsedWorkingDays(DateTimeOffset from, DateTimeOffset to, int mask, IReadOnlyList<PeriodHoliday> holidays)
    {
        if (to <= from) return 0;
        var cursor = from;
        var counted = 0;
        while (counted < 3660)
        {
            cursor = cursor.AddDays(1);
            if (cursor > to) break;
            if (IsWorkingDay(DateOnly.FromDateTime(cursor.Date), mask, holidays))
                counted++;
        }
        return counted;
    }

    // Eight working hours: 08:00-12:00 and 13:30-17:30 in the input offset.
    public static DateTimeOffset AddWorkingHours(DateTimeOffset from, int hours, int mask, IReadOnlyList<PeriodHoliday> holidays)
    {
        if (hours < 0 || hours > 29280) throw new ArgumentOutOfRangeException(nameof(hours));
        var remaining = TimeSpan.FromHours(hours);
        var current = from;
        while (remaining > TimeSpan.Zero)
        {
            if (!IsWorkingDay(DateOnly.FromDateTime(current.Date), mask, holidays))
            { current = new DateTimeOffset(current.Year, current.Month, current.Day, 8, 0, 0, current.Offset).AddDays(1); continue; }
            var day = new DateTimeOffset(current.Year, current.Month, current.Day, 0, 0, 0, current.Offset);
            foreach (var (start, end) in new[] { (TimeSpan.FromHours(8), TimeSpan.FromHours(12)), (TimeSpan.FromHours(13.5), TimeSpan.FromHours(17.5)) })
            {
                var begin = day + start; var finish = day + end;
                if (current > begin) begin = current;
                if (begin >= finish) continue;
                var available = finish - begin;
                if (remaining <= available) return begin + remaining;
                remaining -= available;
                current = finish;
            }
            current = day.AddDays(1).AddHours(8);
        }
        return from;
    }
}
