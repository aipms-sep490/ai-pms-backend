namespace AIPMS.Domain.Supervisors;

public static class SupervisorSelectionEligibility
{
    public static bool CanRequestPrimary(string? status, bool hasActiveAssignment, bool accepting = false)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var validPrimaryStatus = status is "DRAFT" or "REVISION_REQUIRED" or "APPROVED" or "ACTIVE"
            || (accepting && status == "SUPERVISOR_PENDING");

        return validPrimaryStatus && !hasActiveAssignment;
    }

    public static bool CanRequestMentor(
        string? status,
        bool hasActiveAssignment,
        IReadOnlyList<long>? requiredMajorIds,
        IReadOnlyList<long>? occupiedMentorMajors,
        long? targetMajorId = null)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var validMentorStatus = status is "DRAFT" or "REVISION_REQUIRED" or "ACTIVE";
        if (!validMentorStatus)
            return false;

        if (status == "ACTIVE" && !hasActiveAssignment)
            return false;

        if (requiredMajorIds is null || requiredMajorIds.Count == 0)
            return false;

        if (targetMajorId.HasValue)
        {
            if (!requiredMajorIds.Contains(targetMajorId.Value))
                return false;

            if (occupiedMentorMajors is not null && occupiedMentorMajors.Contains(targetMajorId.Value))
                return false;

            return true;
        }

        return requiredMajorIds.Any(m => occupiedMentorMajors is null || !occupiedMentorMajors.Contains(m));
    }

    public static bool HasAnyEligibleSlot(
        string? status,
        bool hasActiveAssignment,
        IReadOnlyList<long>? requiredMajorIds,
        IReadOnlyList<long>? occupiedMentorMajors)
    {
        return CanRequestPrimary(status, hasActiveAssignment)
            || CanRequestMentor(status, hasActiveAssignment, requiredMajorIds, occupiedMentorMajors);
    }
}
