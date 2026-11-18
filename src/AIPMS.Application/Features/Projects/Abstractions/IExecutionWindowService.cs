using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Projects.Abstractions;

public sealed record ExecutionWindowInfo(
    long ProjectPeriodId,
    string Code,
    string Name,
    DateTime StartAt,
    DateTime EndAt);

public interface IExecutionWindowService
{
    Task<ExecutionWindowInfo?> GetExecutionWindowAsync(long projectId, CancellationToken ct = default);

    Task ValidateTaskDatesAsync(
        long projectId,
        long milestoneId,
        DateTime? startAt,
        DateTime? dueAt,
        CancellationToken ct = default);

    Task ValidateMilestoneDatesAsync(
        long projectId,
        DateOnly? startDate,
        DateOnly? dueDate,
        long? excludeMilestoneId = null,
        CancellationToken ct = default);

    Task ValidateMilestoneDateUpdateImpactAsync(
        long milestoneId,
        DateOnly? newStartDate,
        DateOnly? newDueDate,
        CancellationToken ct = default);

    Task ValidateExecutionWindowUpdateImpactAsync(
        long projectPeriodId,
        DateTime newStartAt,
        DateTime newEndAt,
        CancellationToken ct = default);
}
