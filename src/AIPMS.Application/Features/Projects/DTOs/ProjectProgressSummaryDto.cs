namespace AIPMS.Application.Features.Projects.DTOs;

public sealed record ProjectProgressSummaryDto(
    long ProjectId,
    int TotalTasks,
    int DoneTasks,
    int BlockedTasks,
    int OverdueTasks,
    int TotalMilestones,
    int CompletedMilestones,
    double ProgressPercentage);
