using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IEvaluationRepository
{
    Task<bool> ProjectExistsAsync(long projectId, CancellationToken ct);
    Task<bool> CanEvaluateProjectAsync(long userId, long projectId, CancellationToken ct);
    Task<RubricDto?> GetRubricAsync(long id, CancellationToken ct);
    Task<PagedResult<RubricDto>> ListRubricsAsync(bool? active, int page, int size, CancellationToken ct);
    Task<long> CreateRubricAsync(CreateRubricData data, CancellationToken ct);
    Task<bool> UpdateRubricAsync(long id, UpdateRubricData data, CancellationToken ct);
    Task<bool> DeleteRubricAsync(long id, CancellationToken ct);
    Task<bool> UpsertRubricCriterionAsync(long rubricId, UpsertRubricCriterionData data, CancellationToken ct);
    Task<bool> DeleteRubricCriterionAsync(long rubricId, long rubricCriterionId, CancellationToken ct);
    Task<bool> ReorderRubricCriteriaAsync(long rubricId, IReadOnlyList<long> orderedIds, CancellationToken ct);
    Task<bool> SetRubricActiveAsync(long rubricId, bool active, long actorId, DateTime at, CancellationToken ct);
    Task<long> CreateDraftAsync(long projectId, long evaluatorId, long rubricId,
        string evaluationType, string? evidenceSummary, CancellationToken ct);
    Task<EvaluationDetailDto?> GetAsync(long id, CancellationToken ct);
    Task<PagedResult<EvaluationDto>> GetByProjectAsync(long projectId, int page, int size, CancellationToken ct);
    Task<bool> UpsertScoreAsync(long evaluationId, long rubricCriterionId, decimal score,
        string? comments, CancellationToken ct);
    Task<bool> UpdateCommentsAsync(long id, string? comments, string? evidenceSummary, CancellationToken ct);
    Task<bool> FinalizeAsync(long id, decimal total, long actorId, DateTime at, CancellationToken ct);
}

