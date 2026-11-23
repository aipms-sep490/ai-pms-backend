namespace AIPMS.Application.Features.Academic;

public sealed record PreSemesterReadinessItemDto(
    string Key,
    string Label,
    string Description,
    string Status,
    int TotalCount,
    int DraftCount,
    int PublishedCount,
    string Route,
    bool SupportsRollover,
    bool SupportsAiDraft,
    bool AllowedByRole);

public sealed record PreSemesterReadinessDto(
    long SemesterId,
    string SemesterCode,
    string SemesterName,
    string SemesterStatus,
    long? DepartmentId,
    string? DepartmentName,
    bool IsAdmin,
    bool IsDepartmentStaff,
    int ReadyCount,
    int TotalRelevantCount,
    IReadOnlyList<PreSemesterReadinessItemDto> Items);

public sealed record RolloverPreviewRequest(
    long SourceSemesterId,
    long TargetSemesterId,
    long? DepartmentId = null,
    bool IncludeRubrics = true,
    bool IncludeReviewGates = true,
    bool IncludeColdTemplates = true,
    bool IncludePolicies = true,
    bool ShiftDates = true,
    bool LoadVietnamHolidays = false);

public sealed record RolloverDateShiftDto(
    string Label,
    string? OldStart,
    string? OldEnd,
    string? NewStart,
    string? NewEnd);

public sealed record RolloverPreviewItemDto(
    string Category,
    long SourceId,
    string SourceCode,
    string SourceName,
    string ProposedCode,
    string ProposedName,
    long? TargetPeriodId,
    string? TargetPeriodName,
    bool WillSkip,
    string? SkipReason,
    IReadOnlyList<RolloverDateShiftDto> DateShifts);

public sealed record RolloverPreviewDto(
    long SourceSemesterId,
    string SourceSemesterCode,
    long TargetSemesterId,
    string TargetSemesterCode,
    int ShiftDays,
    int TotalCandidates,
    int ActionableCount,
    IReadOnlyList<RolloverPreviewItemDto> Items,
    IReadOnlyList<string> Warnings);

public sealed record RolloverApplyRequest(
    Guid IdempotencyKey,
    long SourceSemesterId,
    long TargetSemesterId,
    long? DepartmentId = null,
    bool IncludeRubrics = true,
    bool IncludeReviewGates = true,
    bool IncludeColdTemplates = true,
    bool IncludePolicies = true,
    bool ShiftDates = true,
    bool LoadVietnamHolidays = false);

public sealed record RolloverCreatedItemDto(
    string Category,
    long SourceId,
    long CreatedId,
    string Code,
    string Name,
    string Status);

public sealed record RolloverSkippedItemDto(
    string Category,
    long SourceId,
    string Name,
    string Reason);

public sealed record RolloverApplyResultDto(
    bool Replay,
    int CreatedCount,
    int SkippedCount,
    int HolidaysLoadedCount,
    IReadOnlyList<RolloverCreatedItemDto> Created,
    IReadOnlyList<RolloverSkippedItemDto> Skipped,
    IReadOnlyList<string> Warnings);

public sealed record PreSemesterDiffFindingDto(
    string Area,
    string Severity,
    string Title,
    string CurrentState,
    string DocumentRequirement,
    string RecommendedAction,
    string? Route);

public sealed record PreSemesterAiDiffResponseDto(
    string SourceFileName,
    long TargetSemesterId,
    string Summary,
    bool UsedAi,
    IReadOnlyList<PreSemesterDiffFindingDto> Findings,
    IReadOnlyList<string> Warnings);
