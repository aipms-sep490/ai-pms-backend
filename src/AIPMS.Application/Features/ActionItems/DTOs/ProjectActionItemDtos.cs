using System;

namespace AIPMS.Application.Features.ActionItems.DTOs;

public sealed record ProjectActionItemDto(
    long Id,
    long ProjectId,
    string SourceType,
    long? MeetingId,
    long? ProgressReportId,
    string Title,
    string? Description,
    long? OwnerId,
    string? OwnerName,
    long? TaskId,
    string? TaskTitle,
    long? MilestoneId,
    string? MilestoneTitle,
    DateTime? DueAt,
    string Status,
    long CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string ConcurrencyToken);

public sealed record CreateProjectActionItemRequest(
    string SourceType,
    string Title,
    long? MeetingId = null,
    long? ProgressReportId = null,
    string? Description = null,
    long? OwnerId = null,
    long? TaskId = null,
    long? MilestoneId = null,
    DateTime? DueAt = null);

public sealed record UpdateProjectActionItemRequest(
    string Title,
    string? Description = null,
    long? OwnerId = null,
    long? TaskId = null,
    long? MilestoneId = null,
    DateTime? DueAt = null,
    string? ConcurrencyToken = null);

public sealed record UpdateProjectActionItemStatusRequest(
    string Status,
    string? ConcurrencyToken = null);
