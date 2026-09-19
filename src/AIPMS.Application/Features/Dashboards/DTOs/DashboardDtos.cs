using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.WorkflowContext.DTOs;

namespace AIPMS.Application.Features.Dashboards.DTOs;

public sealed record DashboardProjectDto(long Id, string Code, string Title, string Status, long TeamId,
    long SemesterId, int PendingProgressReviews, ProjectProgressAnalysisDto Analysis);
public sealed record DashboardTaskDto(long Id, string Title, string Status, DateTime DueAtUtc, bool IsOverdue);
public sealed record DashboardMilestoneDto(long Id, string Title, string Status, DateOnly DueDate, bool IsOverdue);
public sealed record StudentDashboardDto(DateTimeOffset AsOfUtc, UserWorkflowContextDto Context,
    IReadOnlyList<WorkflowActionDto> Actions, DashboardProjectDto? Project, int UnreadNotifications,
    int AssignedOpenTasks, int AssignedOverdueTasks, IReadOnlyList<DashboardTaskDto> TaskDeadlines,
    IReadOnlyList<DashboardMilestoneDto> MilestoneDeadlines, ContributionMemberDto? OwnContribution,
    string ContributionDataStatus);
public sealed record DashboardStatusCountDto(string Status, int Count);
public sealed record SupervisorWorkloadDto(bool HasProfile, bool IsAvailable, int AssignedProjects,
    int? ProfileMaxActiveProjects);
public sealed record SupervisorDashboardDto(DateTimeOffset AsOfUtc, SupervisorWorkloadDto Workload,
    IReadOnlyList<DashboardStatusCountDto> ProjectStates, int PendingProgressReviews,
    int OverdueTasks, PagedResult<DashboardProjectDto> Projects);
