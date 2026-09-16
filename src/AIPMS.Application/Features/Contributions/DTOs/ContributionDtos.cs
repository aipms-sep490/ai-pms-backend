namespace AIPMS.Application.Features.Contributions.DTOs;

public sealed record ContributionMemberDto(long UserId, string DisplayName, int AssignedTasks, int CompletedTasks,
    int SubmittedReports, int AttendedMeetings, int SubmittedDeliverableVersions, double ActivityScore);
