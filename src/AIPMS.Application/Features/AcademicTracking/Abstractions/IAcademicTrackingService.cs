using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.AcademicTracking.Abstractions;

public interface IAcademicTrackingService
{
    Task<EvidenceReviewTarget> ReviewTarget(long evidenceId, long? sourceVersionId, CancellationToken ct);
    Task<EvidenceReviewDto> Review(long evidenceId, ReviewEvidenceRequest input, CancellationToken ct);
    Task<PagedResult<EvidenceReviewDto>> Reviews(long evidenceId, int page, int pageSize, CancellationToken ct);
    Task<CheckpointDto> SaveCheckpoint(long projectId, long? id, SaveCheckpointRequest input, CancellationToken ct);
    Task<CheckpointDto> DecideCheckpoint(long id, DecideCheckpointRequest input, CancellationToken ct);
    Task<PagedResult<CheckpointDto>> Checkpoints(long projectId, int page, int pageSize, CancellationToken ct);
    Task<CommitteeDto> CreateCommittee(long projectId, CreateCommitteeRequest input, CancellationToken ct);
    Task<PagedResult<CommitteeDto>> Committees(long projectId, int page, int pageSize, CancellationToken ct);
    Task<DefenseSessionDto> ScheduleDefense(long projectId, ScheduleDefenseRequest input, CancellationToken ct);
    Task<DefenseSessionDto> CancelDefense(long id, CancelDefenseRequest input, CancellationToken ct);
    Task<PagedResult<DefenseSessionDto>> DefenseSessions(long projectId, int page, int pageSize, CancellationToken ct);
    Task<DefenseSessionDto> DefenseSession(long id, CancellationToken ct);
}

