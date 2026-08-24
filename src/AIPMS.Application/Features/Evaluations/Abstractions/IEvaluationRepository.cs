using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.DTOs;
namespace AIPMS.Application.Features.Evaluations.Abstractions;
public interface IEvaluationRepository
{
 Task<bool> ProjectExistsAsync(long projectId,CancellationToken ct); Task<bool> CanEvaluateProjectAsync(long userId,long projectId,CancellationToken ct);
 Task<long> CreateDraftAsync(long projectId,long evaluatorId,string evaluationType,CancellationToken ct); Task<EvaluationDetailDto?> GetAsync(long id,CancellationToken ct);
 Task<PagedResult<EvaluationDto>> GetByProjectAsync(long projectId,int page,int size,CancellationToken ct); Task UpsertScoreAsync(long evaluationId,long criterionId,decimal score,string? comments,CancellationToken ct);
 Task UpdateCommentsAsync(long id,string? comments,CancellationToken ct); Task<bool> FinalizeAsync(long id,decimal total,DateTime at,CancellationToken ct);
}
