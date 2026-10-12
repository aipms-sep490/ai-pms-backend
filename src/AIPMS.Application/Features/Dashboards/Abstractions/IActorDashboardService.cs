using AIPMS.Application.Features.Dashboards.DTOs;

namespace AIPMS.Application.Features.Dashboards.Abstractions;

public interface IActorDashboardService
{
    Task<ActorDashboardDto> ReadAsync(long? semesterId, CancellationToken ct);
}
