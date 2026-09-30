using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IEvaluationSchemeService
{
    Task<AIPMS.Application.Common.Models.PagedResult<EligibleEvaluatorDto>> CandidatesAsync(long projectId, long periodId,
        long componentId, string? scope, long? majorId, long? studentId, int page, int pageSize, CancellationToken ct);
    Task<EvaluationSchemeDto> SaveAsync(long? id, SaveEvaluationSchemeRequest input, CancellationToken ct);
    Task<EvaluationSchemeDto> GetAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<EvaluationSchemeDto>> ListAsync(long projectId, CancellationToken ct);
    Task<EvaluationSchemeDto> PublishAsync(long id, string token, CancellationToken ct);
    Task<EvaluationSchemeDto> VersionAsync(long id, string token, CancellationToken ct);
    Task DeleteAsync(long id, string token, CancellationToken ct);
    Task<ScopedAssignmentContext> ResolveAssignmentAsync(long projectId, AssignEvaluatorRequest input, CancellationToken ct);
    Task EnsureWritableAsync(EvaluationAssignmentRecord assignment, CancellationToken ct);
    Task<ProjectResultPreviewDto> PreviewAsync(long projectId, long? studentId, CancellationToken ct);
    Task<ProjectResultDto> PublishProjectAsync(long projectId, string token, CancellationToken ct);
    Task<StudentResultDto> PublishStudentAsync(long projectId, long studentId, string token, CancellationToken ct);
    Task<StudentResultDto> GetStudentAsync(long projectId, long studentId, CancellationToken ct);
}
