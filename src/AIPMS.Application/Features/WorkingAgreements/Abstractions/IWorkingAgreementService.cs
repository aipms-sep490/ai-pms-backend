using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;

namespace AIPMS.Application.Features.WorkingAgreements.Abstractions;

public interface IWorkingAgreementService
{
    Task<WorkingAgreementDto?> Get(long projectId, CancellationToken ct);
    Task<PagedResult<WorkingAgreementDto>> History(long projectId, int page, int pageSize, CancellationToken ct);
    Task<WorkingAgreementDto> Save(long projectId, SaveWorkingAgreementRequest input, CancellationToken ct);
    Task<WorkingAgreementDto> Publish(long projectId, AgreementTokenRequest input, CancellationToken ct);
    Task<WorkingAgreementDto> Accept(long projectId, AgreementTokenRequest input, CancellationToken ct);
    Task<IReadOnlyList<ExecutionActionDto>> Capabilities(long projectId, CancellationToken ct);
    Task<WorkingAgreementReadiness?> Readiness(long projectId, CancellationToken ct);
}
