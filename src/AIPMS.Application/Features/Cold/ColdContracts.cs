using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;

namespace AIPMS.Application.Features.Cold;

public sealed record ColdReportInput(string Code, string Name, bool Required = true);
public sealed record CreateColdTemplateRequest(long MajorId, long ProjectPeriodId, string Name, IReadOnlyList<ColdReportInput> Reports);
public sealed record ColdTemplateDto(long Id, long MajorId, long ProjectPeriodId, string Name, int Version, IReadOnlyList<ColdReportInput> Reports);
public sealed record ColdTemplateTarget(long TemplateId, string Scope, long? StudentId = null);
public sealed record ApplyColdChecklistRequest(long ProjectPeriodId, DateTimeOffset Deadline, IReadOnlyList<ColdTemplateTarget> Targets);
public sealed record ColdTokenRequest(string ConcurrencyToken);
public sealed record SaveColdDraftRequest(int ExpectedLatestVersion, string? Note = null, string? ConcurrencyToken = null);
public sealed record ColdFileDto(long Id, string FileName, string ContentType, long SizeBytes, string Sha256, string DownloadUrl);
public sealed record ColdSubmissionDto(long Id, long ChecklistItemId, long ProjectId, string ReportType, string Scope, long MajorId,
    long? StudentId, int Version, string Status, string? Note, long CreatedBy, DateTime CreatedAt, DateTime? SubmittedAt,
    DateTime? LockedAt, string ConcurrencyToken, IReadOnlyList<ColdFileDto> Files);
public sealed record ColdChecklistItemDto(long Id, long TemplateId, int TemplateVersion, string ReportType, string Name,
    bool Required, string Scope, long MajorId, long? StudentId, ColdSubmissionDto? Latest, bool CanWrite);
public sealed record ColdChecklistDto(long Id, long ProjectId, long ProjectPeriodId, DateTime Deadline, string Status,
    string ConcurrencyToken, DateTime? LockedAt, bool CanLock, IReadOnlyList<string> Blockers, IReadOnlyList<ColdChecklistItemDto> Items);

public sealed record ColdGradingProgressDto(long ComponentId, string Name, string Scope, long? MajorId, long? StudentId,
    int RequiredEvaluators, int AssignedEvaluators, int FinalizedEvaluators, decimal? Score, bool ContributesToResult);
public sealed record ColdProgressDto(long ProjectId, string PackageStatus, int VisibleReports, int SubmittedReports, int LockedReports,
    bool Ready, IReadOnlyList<string> Blockers, IReadOnlyList<ColdGradingProgressDto> Grading);

public static class ColdTemplateExamples
{
    // Explicit examples only: no global IT checklist is imposed on other majors.
    public static IReadOnlyList<ColdReportInput> ItReports { get; } = [
        new("PROJECT_INTRODUCTION", "Project Introduction"), new("PROJECT_MANAGEMENT", "Project Management"),
        new("SOFTWARE_REQUIREMENT", "Software Requirement"), new("USER_GUIDE", "User Guide"),
        new("SOFTWARE_DESIGN", "Software Design"), new("TESTING", "Testing"), new("IMPLEMENTATION", "Implementation")];
}
