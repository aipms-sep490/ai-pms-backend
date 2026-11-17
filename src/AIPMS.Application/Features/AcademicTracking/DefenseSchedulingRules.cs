namespace AIPMS.Application.Features.AcademicTracking;

public static class DefenseSchedulingRules
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeOnly[] Starts = [new(8, 0), new(9, 30), new(11, 0),
        new(13, 30), new(15, 0), new(16, 30)];

    public static (DateTimeOffset Start, DateTimeOffset End) Slot(DateOnly date, int number)
    {
        if (number is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(number));
        var start = new DateTimeOffset(date.ToDateTime(Starts[number - 1]), VietnamOffset);
        return (start, start.AddMinutes(75));
    }

    public static bool Overlaps(DateTimeOffset aStart, DateTimeOffset aEnd, DateTimeOffset bStart, DateTimeOffset bEnd) =>
        aStart < bEnd && aEnd > bStart;

    /// <summary>
    /// Validates reviewer count and roles for Gate 1, 2, 3:
    /// Requires exactly 2 reviewers from the fixed panel (1 Chair, 1 Secretary).
    /// </summary>
    public static bool IsValidGateReviewerPanel(int gateSequence, int reviewerCount, bool hasChair, bool hasSecretary)
    {
        if (gateSequence is 1 or 2 or 3)
        {
            return reviewerCount == 2 && hasChair && hasSecretary;
        }
        return true;
    }

    /// <summary>
    /// Validates defense committee size for Gate 4:
    /// Gate 4 (Final Defense) requires exactly 4 committee members.
    /// </summary>
    public static bool IsValidGate4CommitteeSize(int memberCount) => memberCount == 4;

    /// <summary>
    /// Gate 4 continuity rule:
    /// At least one of the 4 committee members in Gate 4 must have evaluated this project
    /// in one of the earlier gates (Gate 1, 2, or 3) to maintain academic continuity.
    /// </summary>
    public static bool HasGateContinuity(IEnumerable<long> gate4MemberIds, IEnumerable<long> previousGateReviewerIds)
    {
        ArgumentNullException.ThrowIfNull(gate4MemberIds);
        ArgumentNullException.ThrowIfNull(previousGateReviewerIds);
        return gate4MemberIds.Intersect(previousGateReviewerIds).Any();
    }
}
