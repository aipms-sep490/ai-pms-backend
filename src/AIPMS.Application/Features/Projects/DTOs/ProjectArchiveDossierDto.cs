using System;
using System.Collections.Generic;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.Results.DTOs;

namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ArchiveDossierMemberDto(
    long UserId,
    string? StudentCode,
    string FullName,
    string Email,
    long? MajorId,
    string? MajorName,
    bool IsLeader);

public sealed record ArchiveDossierSupervisorDto(
    long UserId,
    string FullName,
    string Email,
    string AssignmentType,
    bool IsPrimary,
    long? MajorId,
    string? MajorName,
    bool IsActive,
    DateTime AssignedAt,
    DateTime? EndedAt);

public sealed record ArchiveDossierDefenseCommitteeMemberDto(
    long UserId,
    string FullName,
    long MajorId,
    string? MajorName,
    string Role);

public sealed record ArchiveDossierDefenseDto(
    string? CommitteeName,
    string? CommitteeStatus,
    string? Room,
    DateTime? StartAt,
    DateTime? EndAt,
    IReadOnlyList<ArchiveDossierDefenseCommitteeMemberDto> Members);

public sealed record ArchiveDossierFinalFileDto(
    long FileId,
    string FileName,
    string? Sha256,
    long SizeBytes,
    string DownloadUrl,
    long? MajorId,
    string Scope);

public sealed record ArchiveDossierFinalItemDto(
    long DeliverableId,
    string Title,
    long? DeliverableVersionId,
    IReadOnlyList<ArchiveDossierFinalFileDto> Files);

public sealed record ArchiveDossierFinalSubmissionDto(
    long FinalSubmissionId,
    DateTime SubmittedAt,
    DateTime? LockedAt,
    string? LockedByName,
    IReadOnlyList<ArchiveDossierFinalItemDto> Items);

public sealed record ArchiveDossierAppealDto(
    long Id,
    string Source,
    string AppealKind,
    string TargetLabel,
    string? ComponentName,
    string Status,
    int ResultVersion,
    DateTime RequestedAt,
    string? DecisionReason);

public sealed record ArchiveDossierAppealsSummaryDto(
    int TotalCount,
    int AcceptedCount,
    int RejectedCount,
    int WithdrawnCount,
    int PendingCount,
    IReadOnlyList<ArchiveDossierAppealDto> Items);

public sealed record ArchiveDossierResultsDto(
    ProjectResultDto? ProjectResult,
    IReadOnlyList<StudentResultDto> StudentResults,
    IReadOnlyList<ResultRevisionSummary> RevisionHistory);

public sealed record ProjectArchiveDossierDto(
    long Id,
    string Code,
    string Title,
    string? Description,
    string Status,
    string? TrackCode,
    string? SemesterCode,
    string? SemesterName,
    long? LeadDepartmentId,
    string? LeadDepartmentName,
    IReadOnlyList<string> DepartmentNames,
    IReadOnlyList<ProjectMajorDto> Majors,
    DateTime RegisteredAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? CompletedAt,
    DateTime? ArchivedAt,
    string ConcurrencyToken,
    bool CanUnarchive,
    IReadOnlyList<ArchiveDossierMemberDto> Members,
    IReadOnlyList<ArchiveDossierSupervisorDto> Supervisors,
    ArchiveDossierResultsDto Results,
    ArchiveDossierDefenseDto? Defense,
    ArchiveDossierFinalSubmissionDto? FinalSubmission,
    ArchiveDossierAppealsSummaryDto Appeals,
    IReadOnlyList<ProjectStatusHistoryDto> StatusHistory);
