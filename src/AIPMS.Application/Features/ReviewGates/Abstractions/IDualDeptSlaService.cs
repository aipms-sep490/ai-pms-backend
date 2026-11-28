namespace AIPMS.Application.Features.ReviewGates.Abstractions;

public interface IDualDeptSlaService
{
    Task<SlaSweepResultDto> SweepAsync(long? actorUserId, CancellationToken ct);
}
