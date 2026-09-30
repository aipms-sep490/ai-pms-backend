using AIPMS.Application.Features.Semesters.DTOs;

namespace AIPMS.Application.Features.Semesters.Abstractions;

public interface IPeriodPolicyService
{
    Task<IReadOnlyList<PeriodPolicyDto>> HistoryAsync(long periodId, CancellationToken ct);
    Task<PeriodPolicyDto> GetAsync(long periodId, DateTimeOffset? asOf, CancellationToken ct);
    Task<PeriodPolicyDto> PutAsync(long periodId, UpdatePeriodPolicyRequest input, CancellationToken ct);
}
