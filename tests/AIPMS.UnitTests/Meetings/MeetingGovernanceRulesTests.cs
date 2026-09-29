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
}
