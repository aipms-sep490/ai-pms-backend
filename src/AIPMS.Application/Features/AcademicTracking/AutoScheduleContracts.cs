namespace AIPMS.Application.Features.AcademicTracking;

public sealed record AutoSchedulePreviewRequest(DateOnly StartDate, DateOnly EndDate,
    IReadOnlyList<long> RoomIds, int MaxSlotsPerDay = 6);
public sealed record DefenseAllocation(long ProjectId, long CommitteeId, long RoomId, string RoomCode,
    DateOnly Date, int SlotNumber, DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record DefenseScheduleConflict(long ProjectId, string Reason);
public sealed record AutoSchedulePreviewDto(int ScheduledCount, int UnscheduledCount,
    IReadOnlyList<DefenseAllocation> Allocations, IReadOnlyList<DefenseScheduleConflict> Conflicts,
    string ConcurrencyToken);
public sealed record AutoScheduleApplyRequest(IReadOnlyList<DefenseAllocation> Allocations, string ConcurrencyToken);
public sealed record AutoScheduleApplyDto(bool Success, int CreatedSessionsCount);
