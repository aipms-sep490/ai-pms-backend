namespace AIPMS.Application.Features.Sprints;

public sealed record SaveSprintRequest(string Name, DateTimeOffset StartAt, DateTimeOffset EndAt, string? ConcurrencyToken = null);
public sealed record SprintTokenRequest(string ConcurrencyToken);
public sealed record CompleteSprintRequest(string ConcurrencyToken, IReadOnlyList<SprintCarryover> Carryover);
public sealed record SprintCarryover(long TaskId, long? TargetSprintId, string TaskConcurrencyToken);
public sealed record SprintDto(long Id, long ProjectId, string Name, string Status, DateTime StartAt, DateTime EndAt,
    DateTime? StartedAt, DateTime? CompletedAt, string ConcurrencyToken, bool CanEdit, bool CanStart, bool CanComplete);
public sealed record SaveTaskBoardRequest(long? SprintId, int? StoryPoints, IReadOnlyList<string> Labels, string ConcurrencyToken);
public sealed record SprintBurndownPoint(DateTime At, int ScopePoints, int RemainingPoints, int UnestimatedTasks);
public sealed record SprintBurndownDto(long SprintId, DateTime? HistoryStartsAt, IReadOnlyList<SprintBurndownPoint> Points, bool Complete);
