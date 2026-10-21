namespace AIPMS.Application.Features.ActionAggregators;

public sealed record ActionAggregatorItem(string Kind, long Id, long? ProjectId, string Title,
    string Status, string Severity, DateTime? DueAt, string? DeepLink);

public sealed record StudentNextActionsDto(DateTime AsOfUtc, long? ProjectId,
    IReadOnlyList<ActionAggregatorItem> Items);

public sealed record SupervisorTriageQueueDto(DateTime AsOfUtc,
    IReadOnlyList<ActionAggregatorItem> Items);

public sealed record DepartmentUrgentActionsDto(DateTime AsOfUtc, long DepartmentId,
    IReadOnlyList<ActionAggregatorItem> Items);
