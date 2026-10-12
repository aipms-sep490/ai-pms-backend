using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.PeerEvaluations.Abstractions;

public interface IPeerEvaluationService
{
    Task<PeerRoundDto> Create(long projectId, CreatePeerRoundRequest input, CancellationToken ct);
    Task<PeerRoundDto> Close(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct);
    Task<PagedResult<PeerRoundDto>> Rounds(long projectId, int page, int pageSize, CancellationToken ct);
    Task<MyPeerEvaluationDto> Mine(long projectId, long? roundId, CancellationToken ct);
    Task<PeerEvaluationDto> Save(long projectId, long roundId, SavePeerEvaluationRequest input, CancellationToken ct);
    Task<PeerEvaluationDto> Submit(long projectId, long roundId, PeerTokenRequest input, CancellationToken ct);
    Task<PagedResult<PeerEvaluationDto>> Review(long projectId, long roundId, int page, int pageSize, CancellationToken ct);
}
