using System;

namespace AIPMS.Application.Features.ProgressReports.DTOs;

public sealed record ReportingCycleDto(
    long Id,
    long ProjectId,
    long ProjectPeriodId,
    string ReportType,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime Deadline,
    string LatePolicy,
    long CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string ConcurrencyToken);

// All date-time instants use DateTimeOffset at the API boundary so callers must
// provide explicit timezone context. Command handlers normalize to UTC (.UtcDateTime)
// before passing values to the repository. Persisted columns are DATETIME2(0) UTC.
public sealed record CreateReportingCycleRequest(
    string ReportType,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    DateTimeOffset Deadline,
    string? LatePolicy = "BLOCK",
    long? ProjectPeriodId = null);

public sealed record UpdateReportingCycleRequest(
    DateTimeOffset? PeriodStart = null,
    DateTimeOffset? PeriodEnd = null,
    DateTimeOffset? Deadline = null,
    string? LatePolicy = null,
    string? ReportType = null,
    string? ConcurrencyToken = null);

public sealed record GenerateReportingCyclesRequest(
    string Cadence = "WEEKLY",
    string LatePolicy = "FLAG",
    int DeadlineOffsetDays = 1,
    bool DryRun = false);

public sealed record GeneratedReportingCycleSlotDto(
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime Deadline);

public sealed record GenerateReportingCyclesResultDto(
    long ProjectPeriodId,
    string Cadence,
    string LatePolicy,
    bool DryRun,
    int ProjectsProcessed,
    int CyclesCreated,
    int CyclesSkipped,
    IReadOnlyList<GeneratedReportingCycleSlotDto> Slots);
