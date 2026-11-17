using AIPMS.Application.Features.ReviewGates;

namespace AIPMS.Application.Features.ReviewGates.Abstractions;

public interface IGateSessionScriptService
{
    Task<GateSessionScriptDto> Get(long gateInstanceId, CancellationToken ct);
    Task<GateSessionScriptDto> Save(long gateInstanceId, SaveGateSessionScriptRequest input, CancellationToken ct);
    Task<GateSessionScriptDto> Sync(long gateInstanceId, SyncGatePresenceRequest input, CancellationToken ct);
}
