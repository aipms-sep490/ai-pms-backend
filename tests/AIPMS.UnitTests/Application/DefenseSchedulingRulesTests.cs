using AIPMS.Application.Features.AcademicTracking;

namespace AIPMS.UnitTests.Application;

public sealed class DefenseSchedulingRulesTests
{
    [Fact]
    public void Six_slots_have_office_hours_and_no_overlap()
    {
        var date = new DateOnly(2026, 11, 20);
        var slots = Enumerable.Range(1, 6).Select(n => DefenseSchedulingRules.Slot(date, n)).ToArray();
        Assert.Equal(new TimeSpan(8, 0, 0), slots[0].Start.TimeOfDay);
        Assert.Equal(new TimeSpan(17, 45, 0), slots[5].End.TimeOfDay);
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(75), s.End - s.Start));
        for (var i = 1; i < slots.Length; i++)
            Assert.False(DefenseSchedulingRules.Overlaps(slots[i - 1].Start, slots[i - 1].End, slots[i].Start, slots[i].End));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefenseSchedulingRules.Slot(date, 7));
    }
}
