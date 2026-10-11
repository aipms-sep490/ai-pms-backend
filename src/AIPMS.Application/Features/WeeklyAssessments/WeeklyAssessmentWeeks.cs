using AIPMS.Application.Common.Exceptions;

namespace AIPMS.Application.Features.WeeklyAssessments;

public static class WeeklyAssessmentWeeks
{
    public static IReadOnlyList<WeeklyAssessmentWeek> Build(DateOnly semesterStart, DateOnly semesterEnd, string zone, DateTimeOffset now)
    {
        if (semesterEnd < semesterStart || semesterEnd.DayNumber - semesterStart.DayNumber > 730)
            throw new ConflictException("A bounded valid semester calendar is required.");
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        var weeks = new List<WeeklyAssessmentWeek>();
        for (var date = semesterStart; date <= semesterEnd; date = date.AddDays(7))
        {
            var end = date.AddDays(6);
            weeks.Add(new(weeks.Count + 1, date, end > semesterEnd ? semesterEnd : end, localToday >= date));
        }
        return weeks;
    }
}
