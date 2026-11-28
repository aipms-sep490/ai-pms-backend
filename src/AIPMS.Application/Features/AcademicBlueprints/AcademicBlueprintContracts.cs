using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.AcademicBlueprints;

public sealed record BlueprintGateRule(
    string Code,
    int Sequence,
    string Title,
    string Objective,
    int SubmissionOffsetWorkingDays,
    int ReviewDurationWorkingDays,
    int RevisionDurationWorkingDays,
    decimal? Weight = null,
    bool BlocksDefense = false,
    IReadOnlyList<string>? RequiredDeliverables = null);

public sealed record BlueprintSprintRule(
    string OriginId,
    string Name,
    int StartOffsetWorkingDays,
    int DurationWorkingDays);

public sealed record BlueprintTaskRule(
    string OriginId,
    string Title,
    string? Description = null,
    string Priority = "HIGH",
    bool IsCoreGateTask = true,
    string? GateCode = null,
    int? DueOffsetWorkingDays = null);

public sealed record BlueprintMilestoneRule(
    string OriginId,
    string Title,
    string? Description,
    int StartOffsetWorkingDays,
    int DueOffsetWorkingDays,
    int SortOrder,
    IReadOnlyList<BlueprintTaskRule>? CoreTasks = null);

public sealed record BlueprintIntegrationDeliverableRule(
    string Code,
    string Title,
    IReadOnlyList<string> Stages);

public sealed record BlueprintEvaluationSchemeTemplate(
    string Name,
    decimal PassThreshold,
    AIPMS.Application.Features.Evaluations.DTOs.SchemePolicyInput Policy,
    IReadOnlyList<AIPMS.Application.Features.Evaluations.DTOs.SchemeComponentInput> Components);

public sealed record AcademicBlueprintConfig(
    IReadOnlyList<BlueprintGateRule>? GateRules = null,
    IReadOnlyList<BlueprintSprintRule>? SprintRules = null,
    IReadOnlyList<BlueprintMilestoneRule>? MilestoneRules = null,
    IReadOnlyList<BlueprintIntegrationDeliverableRule>? IntegrationDeliverables = null,
    string? EvaluationSchemePreset = null,
    BlueprintEvaluationSchemeTemplate? EvaluationSchemeTemplate = null);

public sealed record AcademicBlueprintApprovalDto(
    long DepartmentId,
    string ScopeRole,
    string Decision,
    long? DecidedBy,
    DateTime? DecidedAt,
    string? Notes);

public sealed record AcademicBlueprintDto(
    long Id,
    long AcademicSemesterId,
    string TrackCode,
    string Code,
    string Name,
    int Version,
    string Status,
    long LeadDepartmentId,
    long? ParticipatingDepartmentId,
    long? PeriodPolicyVersionId,
    long? GateTemplateId,
    long? MilestoneTemplateId,
    long? RubricId,
    AcademicBlueprintConfig Config,
    string ConcurrencyToken,
    long CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    IReadOnlyList<AcademicBlueprintApprovalDto> Approvals);

public sealed record CreateAcademicBlueprintRequest(
    long AcademicSemesterId,
    string TrackCode,
    string Code,
    string Name,
    long LeadDepartmentId,
    long? ParticipatingDepartmentId,
    AcademicBlueprintConfig Config,
    long? PeriodPolicyVersionId = null,
    long? GateTemplateId = null,
    long? MilestoneTemplateId = null,
    long? RubricId = null);

public sealed record UpdateAcademicBlueprintRequest(
    string Name,
    long? ParticipatingDepartmentId,
    AcademicBlueprintConfig Config,
    string ConcurrencyToken,
    long? PeriodPolicyVersionId = null,
    long? GateTemplateId = null,
    long? MilestoneTemplateId = null,
    long? RubricId = null);

public sealed record RecordBlueprintApprovalRequest(
    long DepartmentId,
    string Decision,
    string? Notes = null);

public sealed record PlannedGateDiffItem(
    string Code,
    string Title,
    string Action,
    DateTime SubmissionDueAt,
    DateTime ReviewWindowEndAt,
    DateTime RevisionDueAt);

public sealed record PlannedScheduleDiffItem(
    string EntityType,
    string OriginId,
    string Title,
    string Action,
    DateTime? StartAt,
    DateTime? EndAt,
    bool IsCoreGateTask = false);

public sealed record BlueprintDryRunDiffDto(
    long ProjectId,
    long BlueprintId,
    int BlueprintVersion,
    string TrackCode,
    long? ExecutionPeriodId,
    DateTime AnchorDateUtc,
    bool AlreadyApplied,
    IReadOnlyList<PlannedGateDiffItem> Gates,
    IReadOnlyList<PlannedScheduleDiffItem> Sprints,
    IReadOnlyList<PlannedScheduleDiffItem> Milestones,
    IReadOnlyList<PlannedScheduleDiffItem> CoreTasks,
    IReadOnlyList<string> PreservedCustomizedItems);

public sealed record ProjectBlueprintSnapshotDto(
    long Id,
    long ProjectId,
    long BlueprintId,
    int BlueprintVersion,
    long AppliedBy,
    DateTime AppliedAt,
    int GatesCreated,
    int SprintsCreated,
    int MilestonesCreated,
    int TasksCreated,
    bool WasAlreadyApplied);

public sealed record CreateGateDeadlineChangeRequest(
    DateTime RequestedSubmissionDueAt,
    DateTime RequestedReviewEndAt,
    DateTime RequestedRevisionDueAt,
    string Reason);

public sealed record ReviewGateDeadlineChangeRequest(
    string Decision,
    string? ReviewNotes,
    string ConcurrencyToken);

public sealed record GateDeadlineChangeRequestDto(
    long Id,
    long ProjectId,
    long GateInstanceId,
    string GateCode,
    DateTime RequestedSubmissionDueAt,
    DateTime RequestedReviewEndAt,
    DateTime RequestedRevisionDueAt,
    string Reason,
    string Status,
    long RequestedBy,
    long? ReviewedBy,
    string? ReviewNotes,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    string ConcurrencyToken);

public sealed record SemesterReadinessDto(
    long AcademicSemesterId,
    bool IsReadyForRegistration,
    IReadOnlyList<string> PresentPeriodTypes,
    IReadOnlyList<string> MissingPeriodTypes,
    IReadOnlyList<string> PublishedTracks,
    IReadOnlyList<string> MissingOrUnpublishedTracks,
    IReadOnlyList<string> BlockingIssues);

public sealed record SemesterTrackMajorPresetDto(
    long MajorId,
    string MajorCode,
    string MajorName,
    long DepartmentId,
    int MinMembers,
    int MaxMembers,
    string Responsibility);

public sealed record SemesterTrackPresetDto(
    string TrackCode,
    string ProjectMode,
    int MinTeamSize,
    int MaxTeamSize,
    int MinDistinctMajors,
    long? LeadDepartmentId,
    long? ParticipatingDepartmentId,
    IReadOnlyList<SemesterTrackMajorPresetDto> DefaultMajorRequirements,
    bool HasPublishedBlueprint);
