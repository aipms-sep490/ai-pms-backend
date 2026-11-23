using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IIndividualProcessScoringService
{
    Task<ProjectIndividualProcessScoresDto> GetProjectScoresAsync(long projectId, CancellationToken ct);
    Task<GateIndividualProcessScoreItemDto> SaveDraftAsync(long projectId, long gateInstanceId, long studentId, UpsertIndividualProcessScoreRequest input, CancellationToken ct);
    Task<GateIndividualProcessScoreItemDto> FinalizeAsync(long projectId, long gateInstanceId, long studentId, FinalizeIndividualProcessScoreRequest input, CancellationToken ct);
    Task<GateIndividualProcessScoreItemDto> SaveMentorReferenceNoteAsync(long projectId, long gateInstanceId, long studentId, UpsertMentorReferenceNoteRequest input, CancellationToken ct);
}
