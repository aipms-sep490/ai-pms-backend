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

    [Fact]
    public void V4_single_major_has_eight_45m_slots_with_single_15m_mid_session_breaks()
    {
        var date = new DateOnly(2026, 12, 10);
        var slots = DefenseSchedulingRules.AllSlotsV4(date, isInterdisciplinary: false);
        Assert.Equal(8, slots.Count);
        Assert.All(slots, s => Assert.Equal(45, s.DurationMinutes));
        // Morning: 08:00-08:45, 08:45-09:30, (15m break), 09:45-10:30, 10:30-11:15
        Assert.Equal(TimeSpan.FromMinutes(15), slots[2].Start - slots[1].End);
        Assert.Equal(TimeSpan.Zero, slots[1].Start - slots[0].End);
        Assert.True(slots[3].End.TimeOfDay <= new TimeSpan(11, 30, 0));
        // Afternoon: 13:30-14:15, 14:15-15:00, (15m break), 15:15-16:00, 16:00-16:45
        Assert.Equal(new TimeSpan(13, 30, 0), slots[4].Start.TimeOfDay);
        Assert.Equal(TimeSpan.FromMinutes(15), slots[6].Start - slots[5].End);
        Assert.True(slots[7].End.TimeOfDay <= new TimeSpan(17, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefenseSchedulingRules.SlotV4(date, 9, false));
    }

    [Fact]
    public void V4_interdisciplinary_has_six_60m_slots_with_single_15m_mid_session_breaks_and_cold_buffer()
    {
        var date = new DateOnly(2026, 12, 10);
        var slots = DefenseSchedulingRules.AllSlotsV4(date, isInterdisciplinary: true);
        Assert.Equal(6, slots.Count);
        Assert.All(slots, s => Assert.Equal(60, s.DurationMinutes));
        // Morning: 08:00-09:00, (15m break), 09:15-10:15, 10:15-11:15
        Assert.Equal(TimeSpan.FromMinutes(15), slots[1].Start - slots[0].End);
        Assert.Equal(TimeSpan.Zero, slots[2].Start - slots[1].End);
        Assert.True(slots[2].End.TimeOfDay <= new TimeSpan(11, 30, 0));
        // Afternoon: 13:30-14:30, (15m break), 14:45-15:45, 15:45-16:45
        Assert.Equal(TimeSpan.FromMinutes(15), slots[4].Start - slots[3].End);
        Assert.True(slots[5].End.TimeOfDay <= new TimeSpan(17, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DefenseSchedulingRules.SlotV4(date, 7, true));

        var coldEnd = new DateTime(2026, 12, 9, 10, 0, 0, DateTimeKind.Utc);
        Assert.False(DefenseSchedulingRules.MeetsColdBuffer(new DateTimeOffset(coldEnd.AddHours(11)), coldEnd, 12));
        Assert.True(DefenseSchedulingRules.MeetsColdBuffer(new DateTimeOffset(coldEnd.AddHours(12)), coldEnd, 12));
    }
}
