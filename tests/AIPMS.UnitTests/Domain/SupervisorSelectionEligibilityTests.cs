using AIPMS.Domain.Supervisors;

namespace AIPMS.UnitTests.Domain;

public sealed class SupervisorSelectionEligibilityTests
{
    [Theory]
    [InlineData("DRAFT", false, false, true)]
    [InlineData("REVISION_REQUIRED", false, false, true)]
    [InlineData("APPROVED", false, false, true)]
    [InlineData("ACTIVE", false, false, true)]
    [InlineData("SUPERVISOR_PENDING", false, true, true)]
    [InlineData("SUPERVISOR_PENDING", false, false, false)]
    [InlineData("ACTIVE", true, false, false)]
    [InlineData("APPROVED", true, false, false)]
    [InlineData("DRAFT", true, false, false)]
    [InlineData("SUBMITTED", false, false, false)]
    [InlineData("UNDER_REVIEW", false, false, false)]
    [InlineData("COMPLETED", false, false, false)]
    [InlineData("ARCHIVED", false, false, false)]
    public void Primary_selection_follows_eligibility_rules(
        string status, bool hasActiveAssignment, bool accepting, bool expected)
    {
        var result = SupervisorSelectionEligibility.CanRequestPrimary(status, hasActiveAssignment, accepting);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Mentor_selection_allowed_on_active_project_with_primary_and_open_slot()
    {
        var requiredMajors = new long[] { 101, 102 };
        var occupiedMentors = new long[] { 101 };

        // Slot 102 is open -> should be true
        var canRequestAny = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: true, requiredMajors, occupiedMentors);
        Assert.True(canRequestAny);

        var canRequest102 = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: true, requiredMajors, occupiedMentors, targetMajorId: 102);
        Assert.True(canRequest102);

        // Slot 101 is occupied -> should be false
        var canRequest101 = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: true, requiredMajors, occupiedMentors, targetMajorId: 101);
        Assert.False(canRequest101);
    }

    [Fact]
    public void Mentor_selection_blocked_on_active_project_without_primary_supervisor()
    {
        var requiredMajors = new long[] { 101, 102 };
        var occupiedMentors = Array.Empty<long>();

        var result = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: false, requiredMajors, occupiedMentors);
        Assert.False(result);
    }

    [Fact]
    public void Mentor_selection_blocked_when_all_slots_occupied()
    {
        var requiredMajors = new long[] { 101, 102 };
        var occupiedMentors = new long[] { 101, 102 };

        var result = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: true, requiredMajors, occupiedMentors);
        Assert.False(result);
    }

    [Fact]
    public void Mentor_selection_blocked_for_single_major_project()
    {
        var result = SupervisorSelectionEligibility.CanRequestMentor(
            "ACTIVE", hasActiveAssignment: true, requiredMajorIds: null, occupiedMentorMajors: null);
        Assert.False(result);
    }

    [Fact]
    public void HasAnyEligibleSlot_unblocks_interdisciplinary_active_project_with_primary_and_open_mentor_slot()
    {
        // HYB-15 / FUNC-08 core scenario:
        // Project 50004 is ACTIVE, primary supervisor already assigned, 2 mentor majors need supervisors
        var requiredMajors = new long[] { 20002, 20003 };
        var occupiedMentors = Array.Empty<long>();

        var hasSlot = SupervisorSelectionEligibility.HasAnyEligibleSlot(
            status: "ACTIVE",
            hasActiveAssignment: true,
            requiredMajorIds: requiredMajors,
            occupiedMentorMajors: occupiedMentors);

        Assert.True(hasSlot);
    }

    [Fact]
    public void HasAnyEligibleSlot_returns_false_for_single_major_active_project_with_primary()
    {
        // Single major project already has primary supervisor -> no more slots
        var hasSlot = SupervisorSelectionEligibility.HasAnyEligibleSlot(
            status: "ACTIVE",
            hasActiveAssignment: true,
            requiredMajorIds: null,
            occupiedMentorMajors: null);

        Assert.False(hasSlot);
    }
}
