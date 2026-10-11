using AIPMS.Application.Features.WeeklyAssessments;

namespace AIPMS.UnitTests.Application;

public sealed class WeeklyAssessmentWeeksTests
{
    [Fact]
    public void Weeks_follow_semester_start_and_vietnam_local_midnight()
    {
        var start = new DateOnly(2026, 9, 1); var end = new DateOnly(2026, 9, 10);
        var before = WeeklyAssessmentWeeks.Build(start, end, "Asia/Ho_Chi_Minh", new(2026, 9, 7, 16, 59, 59, TimeSpan.Zero));
        Assert.Equal(2, before.Count); Assert.Equal(new DateOnly(2026, 9, 7), before[0].EndDate);
        Assert.False(before[1].HasStarted); Assert.Equal(end, before[1].EndDate);
        var after = WeeklyAssessmentWeeks.Build(start, end, "Asia/Ho_Chi_Minh", new(2026, 9, 7, 17, 0, 0, TimeSpan.Zero));
        Assert.True(after[1].HasStarted);
        Assert.False(WeeklyAssessmentWeeks.Build(start, end, "UTC", new(2026, 9, 7, 17, 0, 0, TimeSpan.Zero))[1].HasStarted);
    }
}
