using System;
using System.Collections.Generic;
using AIPMS.Application.Features.Teams.DTOs;
using AIPMS.Domain.Teams;

namespace AIPMS.Application.Features.Teams.Models;

public sealed record RosterMemberInput(
    long UserId,
    string FullName,
    long? MajorId,
    long? OrganizationId,
    bool IsEligibleStudent,
    bool IsLeader,
    bool QualificationRequired,
    bool QualificationEligible,
    string? QualificationIssueCode,
    DateTime? QualificationValidUntilAt);

public sealed record MajorRequirementInput(
    long MajorId,
    int MinMembers,
    int MaxMembers,
    string Responsibility);

public sealed record AcademicScopeInput(
    long PeriodId,
    string ProjectMode,
    long? PrimaryMajorId,
    long LeadDepartmentId,
    IReadOnlyList<MajorRequirementInput> Requirements);

public sealed record ProjectTagInput(
    string TagType,
    string TagName);

public sealed record ProjectContextInput(
    long? ProjectId,
    string? ProposalSource,
    long? TopicId,
    string? Title,
    string? ProblemStatement,
    string? Objectives,
    string? ExpectedOutput,
    IReadOnlyList<long> MajorIds,
    IReadOnlyList<ProjectTagInput> Tags);

public sealed record TeamEligibilityContextInput(
    long TeamId,
    long ProjectPeriodId,
    long? ProjectId,
    string RoundType,
    long? RevisionHistoryId,
    string ProjectMode,
    string PolicyVersion,
    string RuleVersion,
    IReadOnlyList<RosterMemberInput> Members,
    AcademicScopeInput? Scope,
    ProjectContextInput? Project,
    TeamFormationPolicy Policy);

public sealed record TeamEligibilityHashes(
    string RosterHash,
    string AcademicScopeHash,
    string ProjectContextHash,
    string Fingerprint,
    string TemporalStateHash,
    string EvaluationKey,
    DateTime? ValidUntilAt);

public sealed record TeamEligibilityIssueData(
    int SortOrder,
    string RuleCode,
    string Severity,
    long? MajorId,
    long? UserId,
    string? ExpectedValue,
    string? ActualValue,
    string Message);

public sealed record TeamEligibilitySaveModel(
    long TeamId,
    long ProjectPeriodId,
    long? ProjectId,
    string RoundType,
    long? RevisionHistoryId,
    string ProjectMode,
    string PolicyVersion,
    string RuleVersion,
    TeamEligibilityHashes Hashes,
    string Result,
    long CheckedBy,
    DateTime CheckedAt,
    string TriggerSource,
    IReadOnlyList<TeamEligibilityIssueData> Issues);

public sealed record TeamEligibilitySnapshotData(
    long Id,
    long TeamId,
    long ProjectPeriodId,
    long? ProjectId,
    string RoundType,
    long? RevisionHistoryId,
    string ProjectMode,
    string PolicyVersion,
    string RuleVersion,
    string RosterHash,
    string AcademicScopeHash,
    string ProjectContextHash,
    string Fingerprint,
    string TemporalStateHash,
    string EvaluationKey,
    string Result,
    DateTime? ValidUntilAt,
    long CheckedBy,
    DateTime CheckedAt,
    string TriggerSource,
    IReadOnlyList<TeamEligibilityIssueDto> Issues);

public sealed record TeamEligibilityEvaluationContext(
    long TeamId,
    long ProjectPeriodId,
    long? ProjectId,
    string RoundType,
    long? RevisionHistoryId,
    string ProjectMode,
    string PolicyVersion,
    string RuleVersion,
    TeamEligibilityHashes Hashes);
