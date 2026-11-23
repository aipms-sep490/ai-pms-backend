namespace AIPMS.Application.Features.AcademicTracking;

public sealed record UpdatePeriodDefenseRoomSchedulingRequest(
    bool EnableDefenseRoomScheduling,
    DateTime? ColdWindowEndAt = null,
    int DefenseBufferHours = 12);

public sealed record PeriodDefenseRoomSchedulingDto(
    long PeriodId,
    bool EnableDefenseRoomScheduling,
    DateTime? ColdWindowEndAt,
    int DefenseBufferHours);

public sealed record RecordDefenseTechCheckRequest(
    long ProjectId,
    long RoomId,
    string Status,
    string? Notes = null);

public sealed record DefenseTechCheckDto(
    long Id,
    long ProjectId,
    long RoomId,
    long CheckedBy,
    string Status,
    string? Notes,
    DateTime CheckedAt);

public sealed record ScheduleDefenseV4Request(
    long ProjectId,
    long CommitteeId,
    long RoomId,
    DateOnly Date,
    int SlotNumber);

public sealed record DefenseSessionV4Dto(
    long Id,
    long ProjectId,
    long CommitteeId,
    long? RoomId,
    string RoomCode,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string Status,
    string SchedulePolicyVersion,
    int SlotDurationMinutes,
    string ConcurrencyToken);

public sealed record DefenseSwapCandidateDto(
    long RoomId,
    string RoomCode,
    DateOnly Date,
    int SlotNumber,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    int DurationMinutes);

public sealed record CreateDefenseChangeProposalV4Request(
    long DefenseSessionId,
    string ReasonCategory,
    string ReasonDetails);

public sealed record DefenseChangeProposalV4Dto(
    long DefenseSessionId,
    DateTimeOffset CurrentStartAt,
    DateTimeOffset CurrentEndAt,
    IReadOnlyList<DefenseSwapCandidateDto> SwapCandidates);

public sealed record AddStandbyPoolMemberRequest(
    long ProjectPeriodId,
    long UserId,
    long MajorId,
    long DepartmentId,
    bool IsAvailable = true);

public sealed record StandbyPoolMemberDto(
    long Id,
    long ProjectPeriodId,
    long UserId,
    string FullName,
    long MajorId,
    long DepartmentId,
    bool IsAvailable);

public sealed record StandbySwapRequest(
    long ReplacedUserId,
    long ReplacementUserId,
    string Reason);

public sealed record StandbySwapResultDto(
    long DefenseSessionId,
    long CommitteeId,
    long ReplacedUserId,
    long ReplacementUserId,
    int RevokedAssignmentsCount,
    int CreatedAssignmentsCount);
