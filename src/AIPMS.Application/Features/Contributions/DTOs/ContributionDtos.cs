namespace AIPMS.Application.Features.Contributions.DTOs;

public sealed record ContributionMemberDto(long UserId, string DisplayName, int AssignedTasks, int CompletedTasks,
    int SubmittedReports, int AttendedMeetings, int SubmittedDeliverableVersions, double ActivityScore, int EvidenceCount);
public sealed record ContributionSummaryDto(string DataStatus, double? ActivityVariance, IReadOnlyList<ContributionMemberDto> Members);
