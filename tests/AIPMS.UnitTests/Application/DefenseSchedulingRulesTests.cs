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

    [Theory]
    [InlineData(1, 2, true, true, true)]
    [InlineData(2, 2, true, true, true)]
    [InlineData(3, 2, true, true, true)]
    [InlineData(3, 1, true, false, false)]
    [InlineData(3, 3, true, true, false)]
    [InlineData(3, 2, true, false, false)]
    [InlineData(3, 2, false, true, false)]
    [InlineData(4, 4, true, true, true)]
    public void Gate_reviewer_panel_validation_enforces_rules_for_gates_1_to_3(
        int sequence, int count, bool hasChair, bool hasSecretary, bool expected)
    {
        var valid = DefenseSchedulingRules.IsValidGateReviewerPanel(sequence, count, hasChair, hasSecretary);
        Assert.Equal(expected, valid);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(0, false)]
    public void Gate4_committee_size_must_be_exactly_four(int count, bool expected)
    {
        Assert.Equal(expected, DefenseSchedulingRules.IsValidGate4CommitteeSize(count));
    }

    [Fact]
    public void Gate4_committee_requires_continuity_from_previous_gates()
    {
        long[] previousReviewers = [101, 102];
        long[] committeeWithContinuity = [101, 201, 202, 203];
        long[] committeeWithoutContinuity = [201, 202, 203, 204];

        Assert.True(DefenseSchedulingRules.HasGateContinuity(committeeWithContinuity, previousReviewers));
        Assert.False(DefenseSchedulingRules.HasGateContinuity(committeeWithoutContinuity, previousReviewers));
    }
}
