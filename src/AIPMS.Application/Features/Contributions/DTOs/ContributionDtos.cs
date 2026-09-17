using System;

namespace AIPMS.Application.Features.Contributions.DTOs;

public sealed record ContributionMemberDto(long UserId, string DisplayName, int AssignedTasks, int CompletedTasks,
    int SubmittedReports, int AttendedMeetings, int SubmittedDeliverableVersions, double ActivityScore, int EvidenceCount,
    int UploadedFiles = 0);
public sealed record ContributionSummaryDto(string DataStatus, double? ActivityVariance, IReadOnlyList<ContributionMemberDto> Members,
    int Page = 1, int PageSize = 20, long TotalCount = 0, string RuleVersion = "activity-v2",
    string? SnapshotHash = null, DateTime? SnapshotAt = null);
public sealed record ContributionEvidenceDto(string SourceType, long SourceId, string Label, DateTime OccurredAt,
    double Credit = 1);
public sealed record ContributionMemberEvidence(long UserId, IReadOnlyList<ContributionEvidenceDto> Items);
public sealed record ContributionCapture(string RuleVersion, IReadOnlyList<ContributionMemberDto> Members,
    IReadOnlyList<ContributionMemberEvidence> Evidence);
public sealed record ContributionRebuildResult(ContributionSummaryDto Summary, bool Created);
