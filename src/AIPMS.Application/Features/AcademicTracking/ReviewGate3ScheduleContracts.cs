namespace AIPMS.Application.Features.AcademicTracking;

public sealed record ReviewGate3Allocation(long ProjectId, long GateInstanceId, long RoomId,
    string RoomCode, DateOnly Date, int SlotNumber, DateTimeOffset StartAt, DateTimeOffset EndAt);
public sealed record ReviewGate3SchedulePreviewDto(int ScheduledCount, int UnscheduledCount,
    IReadOnlyList<ReviewGate3Allocation> Allocations, IReadOnlyList<DefenseScheduleConflict> Conflicts,
    string ConcurrencyToken);
public sealed record ReviewGate3ScheduleApplyRequest(IReadOnlyList<ReviewGate3Allocation> Allocations,
    string ConcurrencyToken, IReadOnlyList<RoomSchedulingExclusion>? Exclusions = null);
public sealed record ReviewGate3ScheduleDto(long ProjectId, long GateInstanceId, long RoomId,
    string RoomCode, DateTimeOffset StartAt, DateTimeOffset EndAt, DateTimeOffset PublishedAt,
    long? PublishedBy, string ConcurrencyToken);
public sealed record ReviewGate3ScheduleChangeRequest(long RoomId, DateTimeOffset StartAt,
    DateTimeOffset EndAt, string Reason, string ConcurrencyToken);
