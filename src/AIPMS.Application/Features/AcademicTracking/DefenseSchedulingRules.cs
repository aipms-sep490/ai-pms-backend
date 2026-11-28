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

    /// <summary>
    /// Configurable office slots (Vietnam UTC+7) within Morning 08:00-11:30 & Afternoon 13:30-17:00
    /// supporting 30..120 min durations (defaults: 45m single-major / 60m interdisciplinary per POL-06;
    /// retains 75m legacy layout when durationMinutes == 75).
    /// </summary>
    public static IReadOnlyList<(int SlotNumber, DateTimeOffset Start, DateTimeOffset End, int DurationMinutes)> GenerateOfficeSlots(DateOnly date, int durationMinutes)
    {
        if (durationMinutes is < 30 or > 120)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes), "Slot duration must be between 30 and 120 minutes.");
        if (durationMinutes == 45) return AllSlotsV4(date, false);
        if (durationMinutes == 60) return AllSlotsV4(date, true);
        if (durationMinutes == 75)
            return Enumerable.Range(1, 6).Select(n =>
            {
                var (s, e) = Slot(date, n);
                return (n, s, e, 75);
            }).ToArray();

        var result = new List<(int, DateTimeOffset, DateTimeOffset, int)>();
        var slotNumber = 1;
        foreach (var (windowStart, windowEnd) in new[] { (new TimeOnly(8, 0), new TimeOnly(11, 30)), (new TimeOnly(13, 30), new TimeOnly(17, 0)) })
        {
            var cursor = new DateTimeOffset(date.ToDateTime(windowStart), VietnamOffset);
            var endLimit = new DateTimeOffset(date.ToDateTime(windowEnd), VietnamOffset);
            var sessionIndex = 0;
            while (cursor.AddMinutes(durationMinutes) <= endLimit)
            {
                var end = cursor.AddMinutes(durationMinutes);
                result.Add((slotNumber++, cursor, end, durationMinutes));
                sessionIndex++;
                cursor = sessionIndex == 1 ? end.AddMinutes(15) : end;
            }
        }
        return result;
    }

    public static bool IsValidOfficeWindow(DateTimeOffset start, DateTimeOffset end)
    {
        if (end <= start) return false;
        var duration = (end - start).TotalMinutes;
        if (duration is < 30 or > 120) return false;
        var vnStart = start.ToOffset(VietnamOffset);
        var vnEnd = end.ToOffset(VietnamOffset);
        if (vnStart.Date != vnEnd.Date) return false;
        var day = DateOnly.FromDateTime(vnStart.Date);
        if (Enumerable.Range(1, 6).Any(n => { var s = Slot(day, n); return s.Start == start && s.End == end; }))
            return true;
        var startTime = TimeOnly.FromDateTime(vnStart.DateTime);
        var endTime = TimeOnly.FromDateTime(vnEnd.DateTime);
        var inMorning = startTime >= new TimeOnly(8, 0) && endTime <= new TimeOnly(11, 30);
        var inAfternoon = startTime >= new TimeOnly(13, 30) && endTime <= new TimeOnly(17, 0);
        return inMorning || inAfternoon;
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
