namespace AIPMS.Application.Features.Tasks.DTOs;

public sealed record TaskCriterionDto(
    long Id,
    long TaskId,
    string Title,
    bool IsCompleted,
    int SortOrder,
    DateTime? CompletedAt,
    long? CompletedBy,
    string? CompletedByName);

public sealed record TaskCriteriaListDto(
    long TaskId,
    int TotalCount,
    int CompletedCount,
    bool CanManage,
    bool CanToggle,
    IReadOnlyList<TaskCriterionDto> Items);

public sealed record TaskCriterionInput(string Title, bool? IsCompleted = null);

public sealed record ReplaceTaskCriteriaRequest(IReadOnlyList<TaskCriterionInput> Items);

public sealed record ToggleTaskCriterionRequest(bool IsCompleted);
