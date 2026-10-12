using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Results.Abstractions;

public interface IResultHistoryService
{
    Task<PagedResult<ResultRevisionSummary>> History(long projectId, long? studentId, int page, int pageSize, CancellationToken ct);
    Task<ProjectResultDto> ProjectVersion(long projectId, int version, CancellationToken ct);
    Task<StudentResultDto> StudentVersion(long projectId, long studentId, int version, CancellationToken ct);
}
