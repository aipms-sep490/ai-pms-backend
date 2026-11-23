namespace AIPMS.Application.Features.AcademicTracking;

public static class DefenseSchedulingRules
{
    private static readonly TimeSpan VietnamOffset = TimeSpan.FromHours(7);
    private static readonly TimeOnly[] Starts = [new(8, 0), new(9, 30), new(11, 0),
        new(13, 30), new(15, 0), new(16, 30)];

    private static readonly TimeOnly[] SingleMajorV4Starts = [
        new(8, 0), new(8, 45), new(9, 45), new(10, 30),
        new(13, 30), new(14, 15), new(15, 15), new(16, 0)
    ];

    private static readonly TimeOnly[] InterdisciplinaryV4Starts = [
        new(8, 0), new(9, 15), new(10, 15),
        new(13, 30), new(14, 45), new(15, 45)
    ];

    public const string LegacyPolicyVersion = "LEGACY_75M";
    public const string V4PolicyVersion = "V4_45_60";

    public static (DateTimeOffset Start, DateTimeOffset End) Slot(DateOnly date, int number)
    {
        if (number is < 1 or > 6) throw new ArgumentOutOfRangeException(nameof(number));
        var start = new DateTimeOffset(date.ToDateTime(Starts[number - 1]), VietnamOffset);
        return (start, start.AddMinutes(75));
    }

    /// <summary>
    /// V4.0 Optional Defense Room Scheduler slots (Vietnam UTC+7):
    /// Morning 08:00-11:30 & Afternoon 13:30-17:00 with 1 single 15-minute mid-session break.
    /// Single-major: 45m (8 slots/day: 4 morning + 4 afternoon).
    /// Interdisciplinary: 60m (6 slots/day: 3 morning + 3 afternoon).
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End, int DurationMinutes) SlotV4(DateOnly date, int number, bool isInterdisciplinary)
    {
        var starts = isInterdisciplinary ? InterdisciplinaryV4Starts : SingleMajorV4Starts;
        var duration = isInterdisciplinary ? 60 : 45;
        if (number < 1 || number > starts.Length)
            throw new ArgumentOutOfRangeException(nameof(number), $"Slot number must be 1..{starts.Length}.");
        var start = new DateTimeOffset(date.ToDateTime(starts[number - 1]), VietnamOffset);
        return (start, start.AddMinutes(duration), duration);
    }

    public static IReadOnlyList<(int SlotNumber, DateTimeOffset Start, DateTimeOffset End, int DurationMinutes)> AllSlotsV4(DateOnly date, bool isInterdisciplinary)
    {
        var count = isInterdisciplinary ? InterdisciplinaryV4Starts.Length : SingleMajorV4Starts.Length;
        return Enumerable.Range(1, count).Select(n =>
        {
            var (start, end, dur) = SlotV4(date, n, isInterdisciplinary);
            return (n, start, end, dur);
        }).ToArray();
    }

    public static bool MeetsColdBuffer(DateTimeOffset defenseStartUtc, DateTime? coldWindowEndUtc, int bufferHours)
    {
        if (!coldWindowEndUtc.HasValue) return true;
        var required = new DateTimeOffset(DateTime.SpecifyKind(coldWindowEndUtc.Value, DateTimeKind.Utc)).AddHours(Math.Max(0, bufferHours));
        return defenseStartUtc >= required;
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
