using System.Collections.Generic;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record ProjectEvidencePayload(
    long ProjectId,
    string ProjectStatus,
    int TotalTasks,
    int RetrievedTasks,
    bool TasksTruncated,
    IReadOnlyList<MilestoneEvidenceItem> Milestones,
    IReadOnlyList<TaskEvidenceItem> Tasks,
    IReadOnlyList<ProgressReportEvidenceItem> ProgressReports,
    IReadOnlyList<MeetingEvidenceItem> Meetings,
    ContributionEvidenceItem? ContributionSummary,
    ProjectOverviewEvidence? Overview = null,
    IReadOnlyList<DeliverableEvidenceItem>? Deliverables = null,
    IReadOnlyList<ColdReportEvidenceItem>? ColdReports = null,
    IReadOnlyList<ReviewGateEvidenceItem>? ReviewGates = null,
    IReadOnlyList<ProjectEvidenceLedgerItem>? EvidenceLedger = null,
    ProjectDocumentProgressStats? Stats = null);

public sealed record ProjectOverviewEvidence(
    string ProjectCode,
    string Title,
    string? Description,
    string? ProblemStatement,
    string? Objectives,
    string? ExpectedOutput,
    string AcademicMode,
    IReadOnlyList<ProjectMajorOverviewItem> Majors,
    IReadOnlyList<ProjectMemberOverviewItem> Members,
    IReadOnlyList<ProjectSupervisorOverviewItem> Supervisors);

public sealed record ProjectMajorOverviewItem(long MajorId, string Name, string? Responsibility);
public sealed record ProjectMemberOverviewItem(long UserId, string FullName, string? MajorName, bool IsLeader);
public sealed record ProjectSupervisorOverviewItem(long UserId, string FullName, string AssignmentType, string? MajorName);

public sealed record MilestoneEvidenceItem(
    string Id,
    long MilestoneId,
    string Title,
    string Status,
    string? DueDate,
    int SortOrder,
    string? Description,
    int TotalTasks = 0,
    int DoneTasks = 0);

public sealed record TaskEvidenceItem(
    string Id,
    long TaskId,
    string Title,
    string Status,
    string? Priority,
    string? DueAt,
    bool IsOverdue,
    bool IsBlocked,
    string? Description,
    long? MilestoneId = null,
    string? MilestoneTitle = null,
    IReadOnlyList<string>? AssigneeNames = null,
    IReadOnlyList<string>? MajorNames = null,
    int FileCount = 0,
    int CriteriaTotal = 0,
    int CriteriaCompleted = 0,
    IReadOnlyList<string>? BlockingTaskTitles = null,
    int PendingImpactCount = 0,
    string? ImpactSummary = null,
    string? LatestComment = null);

public sealed record DeliverableEvidenceItem(
    string Id,
    long DeliverableId,
    string Title,
    string? DeliverableType,
    string? MilestoneTitle,
    string EvidenceScope,
    string? TargetMajorName,
    string Status,
    string? DueAt,
    bool IsOverdue,
    int LatestVersionNumber,
    string? LatestVersionStatus,
    string? LatestSubmittedAt,
    IReadOnlyList<string> FileNames,
    string? LatestFeedback);

public sealed record ColdReportEvidenceItem(
    string Id,
    long ChecklistItemId,
    string ReportType,
    string Name,
    string Scope,
    string? MajorName,
    bool Required,
    int LatestVersion,
    string SubmissionStatus,
    int FileCount,
    IReadOnlyList<string> FileNames,
    string? Deadline,
    string? Note);

public sealed record ReviewGateEvidenceItem(
    string Id,
    string GateCode,
    string Title,
    string Status,
    string ReviewEligibility,
    string? WindowEnd,
    IReadOnlyList<ReviewGateRequirementEvidenceItem> Requirements);

public sealed record ReviewGateRequirementEvidenceItem(
    long RequirementId,
    string Code,
    string Title,
    string Type,
    string Scope,
    bool Required,
    string VerificationStatus,
    string? LatestArtifactName,
    string? ReviewerFeedback);

public sealed record ProjectEvidenceLedgerItem(
    string Id,
    long EvidenceId,
    string SourceType,
    long SourceId,
    string? MajorName,
    string VerificationStatus,
    string? Notes);

public sealed record ProjectDocumentProgressStats(
    int TotalTasks,
    int DoneTasks,
    int OverdueTasks,
    int BlockedTasks,
    int ReworkImpactTasks,
    int TotalDeliverables,
    int AcceptedDeliverables,
    int RevisionRequiredDeliverables,
    int PendingDeliverables,
    int TotalColdItems,
    int SubmittedColdItems,
    int DraftColdItems,
    int MissingColdItems,
    int TotalGateRequirements,
    int VerifiedGateRequirements,
    int RejectedGateRequirements,
    int PendingGateRequirements);

public sealed record ProgressReportEvidenceItem(
    string Id,
    long ReportId,
    string ReportType,
    string Status,
    string Period,
    string? Summary,
    string? CompletedWork,
    string? IssuesAndRisks,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record MeetingEvidenceItem(
    string Id,
    long MeetingId,
    string Title,
    string Status,
    string StartAt,
    string? Agenda,
    string? MeetingNotes,
    string? Minutes = null,
    string? Decisions = null,
    string? Blockers = null);

public sealed record ContributionEvidenceItem(
    string Id,
    int MemberCount,
    int TotalTasksCompleted,
    string? SnapshotAt);
