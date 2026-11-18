using AIPMS.Domain.Meetings;

namespace AIPMS.UnitTests.Meetings;

public sealed class MeetingGovernanceRulesTests
{
    [Theory]
    [InlineData("SCHEDULED", false, true)]
    [InlineData("COMPLETED", true, true)]
    [InlineData("CANCELLED", false, false)]
    [InlineData("UNKNOWN", false, false)]
    public void Meeting_state_limits_decisions_and_actions(string status, bool decisions, bool actions)
    {
        Assert.Equal(decisions, MeetingGovernanceRules.CanRecordDecision(status));
        Assert.Equal(actions, MeetingGovernanceRules.CanManageActions(status));
    }

    [Theory]
    [InlineData("OPEN", "IN_PROGRESS", true)]
    [InlineData("IN_PROGRESS", "DONE", true)]
    [InlineData("IN_PROGRESS", "CANCELLED", true)]
    [InlineData("DONE", "OPEN", false)]
    [InlineData("CANCELLED", "IN_PROGRESS", false)]
    [InlineData("DONE", "DONE", true)]
    [InlineData("OPEN", "UNKNOWN", false)]
    public void Terminal_actions_cannot_be_reopened(string before, string after, bool allowed)
        => Assert.Equal(allowed, MeetingGovernanceRules.CanTransitionAction(before, after));

    [Fact]
    public void Action_due_at_validation_rejects_past_and_allows_future()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(MeetingGovernanceRules.IsDueAtValidForNewAction(null, now));
        Assert.True(MeetingGovernanceRules.IsDueAtValidForNewAction(now.AddMinutes(1), now));
        Assert.False(MeetingGovernanceRules.IsDueAtValidForNewAction(now, now));
        Assert.False(MeetingGovernanceRules.IsDueAtValidForNewAction(now.AddMinutes(-1), now));
        Assert.False(MeetingGovernanceRules.IsDueAtValidForNewAction(now.AddDays(-2), now));
    }
}
