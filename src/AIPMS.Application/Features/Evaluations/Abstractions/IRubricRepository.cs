using System;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Abstractions;

public interface IRubricRepository
{
    Task<RubricActor?> GetActorAsync(long userId, CancellationToken ct);
    Task<RubricScope?> GetScopeAsync(long departmentId, long semesterId, CancellationToken ct);
    Task<PagedResult<RubricRecord>> ListAsync(RubricActor actor, RubricFilter filter, CancellationToken ct);
    Task<RubricRecord?> GetAsync(long id, RubricActor actor, bool forUpdate, CancellationToken ct);
    Task<RubricRecord> CreateAsync(long departmentId, long semesterId, string code, string name,
        string? description, IReadOnlyList<RubricCriterionInput> criteria, long actorId, DateTime now,
        long? sourceId, long? majorId, string? projectMode, string? evaluationStage, string? evaluationScope,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, CancellationToken ct, long? participatingDepartmentId = null);
    Task<RubricRecord> UpdateAsync(long id, string name, string? description,
        IReadOnlyList<RubricCriterionInput> criteria, DateTime now,
        long? majorId, string? projectMode, string? evaluationStage, string? evaluationScope,
        DateOnly? effectiveFrom, DateOnly? effectiveTo, CancellationToken ct, long? participatingDepartmentId = null);
    Task<RubricRecord> SetStatusAsync(long id, string status, DateTime now, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct);
    Task<long?> GetMajorDepartmentIdAsync(long majorId, CancellationToken ct);
    Task<RubricApprovalRecord?> GetLatestApprovalAsync(long rubricId, CancellationToken ct);
    Task SubmitForApprovalAsync(long rubricId, long actorId, DateTime now, CancellationToken ct);
    Task ApproveAsync(long rubricId, long approvalId, long actorId, string? approvalReference, DateTime now, CancellationToken ct);
    Task RejectAsync(long rubricId, long approvalId, long actorId, string reason, DateTime now, CancellationToken ct);

    Task<int> CountActiveDepartmentStaffAsync(long departmentId, CancellationToken ct) => Task.FromResult(2);
    Task<bool> DepartmentInSameOrganizationAsync(long departmentIdA, long departmentIdB, CancellationToken ct) => Task.FromResult(true);
    async Task<IReadOnlyList<RubricApprovalRecord>> GetCurrentRoundApprovalsAsync(long rubricId, CancellationToken ct)
    {
        var latest = await GetLatestApprovalAsync(rubricId, ct);
        return latest is null ? [] : [latest];
    }
    Task ApproveAsync(long rubricId, long approvalId, long actorId, string? approvalReference, bool isSingleStaffSelfApproval, DateTime now, CancellationToken ct) =>
        ApproveAsync(rubricId, approvalId, actorId, approvalReference, now, ct);
    Task NotifyApprovalRequestedAsync(long rubricId, long actorId, string rubricCode, string rubricName, IReadOnlyCollection<long> departmentIds, DateTime now, CancellationToken ct) =>
        Task.CompletedTask;
}
