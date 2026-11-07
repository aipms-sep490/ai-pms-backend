namespace AIPMS.Application.Features.AcademicTracking;

public static class DefenseSchedulingRules
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeOnly[] Starts = [new(8, 0), new(9, 30), new(11, 0),
        new(13, 30), new(15, 0), new(16, 30)];

    public static (DateTimeOffset Start, DateTimeOffset End) Slot(DateOnly date, int number)
    {
        if (number is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(number));
        var start = new DateTimeOffset(date.ToDateTime(Starts[number - 1]), VietnamOffset);
        return (start, start.AddMinutes(75));
    }

    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && aEnd > bStart;
}
