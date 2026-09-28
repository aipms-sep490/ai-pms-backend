namespace AIPMS.Domain.Meetings;

public static class MeetingGovernanceRules
{
    public static bool CanRecordDecision(string status) => status == "COMPLETED";
    public static bool CanManageActions(string status) => status is "SCHEDULED" or "COMPLETED";
    public static bool IsActionStatus(string status) => status is "OPEN" or "IN_PROGRESS" or "DONE" or "CANCELLED";
    public static bool CanTransitionAction(string before, string after) => IsActionStatus(after)
        && (before == after || before is "OPEN" or "IN_PROGRESS");
}
