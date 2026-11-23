using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.ProjectChecklists;

public sealed record MasterChecklistItemDefinition(
    string ItemCode,
    string Section,
    string TrackScope,
    long? MajorId,
    string ConditionTag,
    string CoreQuestion,
    string Priority,
    decimal Weight);

public sealed record MasterChecklistTemplateDto(
    long Id,
    long AcademicSemesterId,
    string TrackCode,
    string Code,
    string Name,
    int Version,
    string Status,
    long LeadDepartmentId,
    IReadOnlyList<MasterChecklistItemDefinition> Items,
    string ConcurrencyToken,
    DateTime CreatedAt,
    DateTime? PublishedAt);

public sealed record CreateMasterChecklistTemplateRequest(
    long AcademicSemesterId,
    string TrackCode,
    string Code,
    string Name,
    long LeadDepartmentId,
    IReadOnlyList<MasterChecklistItemDefinition> Items);

public sealed record ProjectChecklistItemDto(
    long Id,
    string ItemCode,
    string Section,
    string TrackScope,
    long? MajorId,
    string ConditionTag,
    string CoreQuestion,
    string Priority,
    decimal Weight,
    bool IsApplicable,
    string? NaReason,
    string? TailoredCheckpoints,
    string? TailoredEvidence,
    string? ReviewerFocusPrompt,
    string? SourceRef,
    string? AdvisoryStatus,
    string? AdvisoryNotes,
    long? AdvisoryBy,
    decimal? ReviewerScore,
    string? ReviewerNotes,
    long? ReviewerBy);

public sealed record ChecklistNormalizedScoreDto(
    decimal TotalApplicableWeight,
    decimal EarnedWeightedScore,
    decimal? NormalizedScoreOutOf10,
    string RatingBand,
    int ApplicableCount,
    int ScoredCount,
    int NaCount);

public sealed record ProjectGateChecklistDto(
    long Id,
    long ProjectId,
    long GateInstanceId,
    string GateCode,
    long? TemplateId,
    int Version,
    string Status,
    string AdvisorRecommendation,
    string ReviewerRecommendation,
    string Review3ConsensusStatus,
    long? PublishedBy,
    DateTime? PublishedAt,
    string ConcurrencyToken,
    ChecklistNormalizedScoreDto? ScoreSummary,
    IReadOnlyList<ProjectChecklistItemDto> Items);

public sealed record TailorChecklistItemPatch(
    string ItemCode,
    bool IsApplicable,
    string? NaReason,
    string? TailoredCheckpoints,
    string? TailoredEvidence,
    string? ReviewerFocusPrompt,
    string? SourceRef,
    decimal? Weight = null);

public sealed record TailorProjectChecklistRequest(
    string ConcurrencyToken,
    IReadOnlyList<TailorChecklistItemPatch> Items);

public sealed record ChecklistDiffEntryDto(
    string ItemCode,
    string Section,
    string Priority,
    bool IsApplicable,
    string? NaReason,
    bool HasTailoredCheckpoints,
    bool HasTailoredEvidence,
    bool HasReviewerFocusPrompt,
    string CoreQuestion,
    string? TailoredCheckpoints,
    string? TailoredEvidence);

public sealed record ChecklistDiffPreviewDto(
    long ChecklistId,
    int Version,
    string Status,
    int TotalItems,
    int ApplicableItems,
    int NaItems,
    int TailoredItems,
    IReadOnlyList<ChecklistDiffEntryDto> Entries);

public sealed record AdvisoryItemScoreInput(string ItemCode, string Status, string? Notes);

public sealed record SubmitAdvisoryEvaluationRequest(
    IReadOnlyList<AdvisoryItemScoreInput> Items,
    string? AdvisorRecommendation = null);

public sealed record ReviewerItemScoreInput(string ItemCode, decimal Score, string? Notes);

public sealed record SubmitReviewerEvaluationRequest(
    IReadOnlyList<ReviewerItemScoreInput> Items,
    string? ReviewerRecommendation = null);

// Pre-defense 2-step Final Lock & Mid-semester Roster Withdrawal (Spec §6.4.3 & §6.4.4)
public sealed record PreDefenseLockStatusDto(
    long ProjectId,
    string ProjectStatus,
    string LockWorkflowStatus,
    long? LockRequestedBy,
    DateTime? LockRequestedAt,
    long? LockedBySupervisorId,
    long? ForceLockedBy,
    string? ForceLockReason,
    string? ColdManifestSha256,
    long? FinalSubmissionId,
    long? ColdPackageId);

public sealed record SupervisorFinalLockDecisionRequest(
    string Decision, // LOCK | RETURN_TO_OPEN
    string? Notes = null);

public sealed record ForceLockFinalRequest(string Reason);

public sealed record WithdrawRosterMemberRequest(long StudentUserId, string Reason);

public sealed record WithdrawRosterMemberResultDto(
    long ProjectId,
    long StudentUserId,
    int WaivedGateRequirements,
    int WaivedColdChecklistItems,
    DateTime WithdrawnAt);

public sealed record ExpandStudentRequirementsResultDto(
    long ProjectId,
    string GateCode,
    int CreatedRequirementCount,
    int ActiveStudentCount);
