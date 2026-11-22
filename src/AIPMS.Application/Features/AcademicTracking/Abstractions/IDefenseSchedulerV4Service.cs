namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IDefenseSchedulerV4Service
{
    Task<PeriodDefenseRoomSchedulingDto> UpdatePeriodSchedulingConfigAsync(long periodId, UpdatePeriodDefenseRoomSchedulingRequest input, CancellationToken ct);
    Task<DefenseTechCheckDto> RecordTechCheckAsync(RecordDefenseTechCheckRequest input, CancellationToken ct);
    Task<DefenseSessionV4Dto> ScheduleSessionV4Async(ScheduleDefenseV4Request input, CancellationToken ct);
    Task<DefenseChangeProposalV4Dto> ProposeChangeV4Async(CreateDefenseChangeProposalV4Request input, CancellationToken ct);
    Task<StandbyPoolMemberDto> AddStandbyPoolMemberAsync(AddStandbyPoolMemberRequest input, CancellationToken ct);
    Task<IReadOnlyList<StandbyPoolMemberDto>> ListStandbyPoolAsync(long periodId, CancellationToken ct);
    Task<StandbySwapResultDto> StandbySwapAsync(long sessionId, StandbySwapRequest input, CancellationToken ct);
}
