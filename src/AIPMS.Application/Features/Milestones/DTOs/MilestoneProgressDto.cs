namespace AIPMS.Application.Features.Milestones.DTOs;

public sealed record MilestoneProgressDto(
    long MilestoneId,
    string MilestoneTitle,
    int TotalTasks,
    int DoneTasks,
    double ProgressPercentage);
