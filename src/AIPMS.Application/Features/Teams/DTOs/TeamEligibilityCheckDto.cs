using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.Teams.DTOs;

public sealed record TeamEligibilityCheckDto(
    long CheckId,
    long TeamId,
    long? ProjectId,
    long ProjectPeriodId,
    string RoundType,
    long? RevisionHistoryId,
    string ProjectMode,
    string PolicyVersion,
    string RuleVersion,
    string Result,
    string Freshness,
    DateTime CheckedAt,
    DateTime? ValidUntilAt,
    long CheckedBy,
    string TriggerSource,
    IReadOnlyList<TeamEligibilityIssueDto> Issues);

public sealed record TeamEligibilityIssueDto(
    long Id,
    long EligibilityCheckId,
    int SortOrder,
    string RuleCode,
    string Severity,
    long? MajorId,
    long? UserId,
    string? ExpectedValue,
    string? ActualValue,
    string Message,
    DateTime CreatedAt);
