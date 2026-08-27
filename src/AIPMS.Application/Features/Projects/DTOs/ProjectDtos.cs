using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectDto(
    long Id,
    long TeamId,
    string TeamName,
    string Code,
    string Title,
    string? Description,
    string? Objectives,
    string Status,
    DateTime RegisteredAt,
    DateTime? SubmittedAt,
    DateTime? ApprovedAt,
    DateTime? CompletedAt,
    long CreatedBy,
    string CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? ProblemStatement,
    string? ExpectedOutput,
    string ConcurrencyToken,
    IReadOnlyList<ProjectMajorDto> Majors,
    IReadOnlyList<ProjectTagDto> Tags);

public sealed record ProjectSummaryDto(
    long Id,
    long TeamId,
    string TeamName,
    string Code,
    string Title,
    string Status,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    IReadOnlyList<ProjectMajorDto> Majors,
    IReadOnlyList<ProjectTagDto> Tags);

public sealed record ProjectMajorDto(
    long Id,
    long MajorId,
    string MajorCode,
    string MajorName);

public sealed record ProjectTagDto(
    long Id,
    string Name,
    string TagType);

public sealed record ProjectStatusHistoryDto(
    long Id,
    long ProjectId,
    string? OldStatus,
    string NewStatus,
    long ChangedBy,
    string ChangedByName,
    string? Reason,
    DateTime ChangedAt);

public sealed record CreateProjectDraftRequest(
    string Title,
    string? Description,
    string? Objectives,
    string? ProblemStatement,
    string? ExpectedOutput,
    IReadOnlyList<long> RequiredMajorIds,
    string Domain,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords);

public sealed record UpdateProjectDraftRequest(
    string ConcurrencyToken,
    string Title,
    string? Description,
    string? Objectives,
    string? ProblemStatement,
    string? ExpectedOutput,
    IReadOnlyList<long> RequiredMajorIds,
    string Domain,
    IReadOnlyList<string> Technologies,
    IReadOnlyList<string> Keywords);

public sealed record SetProjectMajorsRequest(
    string ConcurrencyToken,
    IReadOnlyList<long> RequiredMajorIds);

public sealed record SubmitProjectRequest(
    string ConcurrencyToken);

public sealed record ProjectReviewRequest(
    string ConcurrencyToken,
    string? Reason);
