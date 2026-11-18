namespace AIPMS.Application.Features.Tasks.DTOs;

public sealed record TaskDependencyImpactDto(
    long Id, long TaskId, long PredecessorTaskId, string PredecessorTitle,
    string PredecessorStatus, string Status, DateTime ReopenedAt, long ReopenedBy,
    string ReopenReason, DateTime? ReviewedAt, long? ReviewedBy, string? ReviewReason,
    bool CanResolve, string? BlockedReason);

public sealed record TaskDependencyImpactsDto(
    long TaskId, bool CanReview, IReadOnlyList<TaskDependencyImpactDto> Items);
