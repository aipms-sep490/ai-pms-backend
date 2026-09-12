using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IEvaluationDraftRepository
{
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task<EvaluationActor?> GetActorAsync(long id, CancellationToken ct);
    Task<EvaluationProject?> GetProjectAsync(long id, CancellationToken ct);
    Task LockProjectAsync(long id, CancellationToken ct);
    Task<bool> HasLockedSubmissionAsync(long projectId, CancellationToken ct);
    Task<EvaluationPeriod?> GetPeriodAsync(long id, DateTime now, CancellationToken ct);
    Task<bool> IsCurrentSupervisorAsync(long projectId, long userId, CancellationToken ct);
    Task<EvaluationAssignmentRecord?> GetAssignmentAsync(long id, CancellationToken ct);
    Task<EvaluationAssignmentRecord> AssignAsync(long projectId, long evaluatorId, long periodId,
        long rubricId, long departmentId, string type, long actorId, DateTime now, CancellationToken ct);
    Task<EvaluationAssignmentRecord> RevokeAsync(long id, string reason, DateTime now, CancellationToken ct);
    Task<PagedResult<EvaluationAssignmentRecord>> ListAssignmentsAsync(long? projectId, EvaluationActor actor,
        string? status, int page, int pageSize, CancellationToken ct);
    Task<EvaluationDraftRecord?> GetDraftAsync(long id, CancellationToken ct);
    Task<EvaluationDraftRecord?> FindDraftAsync(long assignmentId, CancellationToken ct);
    Task<EvaluationDraftRecord> CreateDraftAsync(EvaluationAssignmentRecord assignment, DateTime now, CancellationToken ct);
    Task<EvaluationDraftRecord> SaveAsync(long id, IReadOnlyList<EvaluationScoreInput> scores,
        string? comments, decimal? total, DateTime now, CancellationToken ct);
    Task<PagedResult<EvaluationDraftRecord>> ListDraftsAsync(long projectId, EvaluationActor actor,
        int page, int pageSize, CancellationToken ct);
}
