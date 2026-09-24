using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.WorkflowContext.DTOs;

namespace AIPMS.Application.Features.Dashboards.DTOs;

public sealed record DashboardProjectDto(long Id, string Code, string Title, string Status, long TeamId,
    long SemesterId, int PendingProgressReviews, ProjectProgressAnalysisDto Analysis,
    long? DepartmentId = null, string? DepartmentName = null,
    IReadOnlyList<DashboardMajorDto>? Majors = null, DashboardSupervisorDto? Supervisor = null);
public sealed record DashboardMajorDto(long Id, string Code, string Name);
public sealed record DashboardSupervisorDto(long UserId, string Name);
public sealed record DashboardMajorSummaryDto(long MajorId, string Code, string Name, long ProjectCount);
public sealed record DashboardSupervisorSummaryDto(long? UserId, string? Name, long ProjectCount, int OverdueTasks,
    int PendingProgressReviews);
public sealed record DashboardPortfolioSummaryDto(long TotalProjects, IReadOnlyList<DashboardStatusCountDto> ProjectStates,
    IReadOnlyList<DashboardMajorSummaryDto> Majors, IReadOnlyList<DashboardSupervisorSummaryDto> Supervisors,
    IReadOnlyList<DashboardStatusCountDto> RiskLevels);
public sealed record PortfolioDashboardDto(DateTimeOffset AsOfUtc, long? DepartmentId,
    DashboardPortfolioSummaryDto Summary, PagedResult<DashboardProjectDto> Projects);
public sealed record DashboardPortfolioFilter(long? SemesterId, long? DepartmentId, long? MajorId,
    string? Status, string? Search, int Page = 1, int PageSize = 20);
public sealed record DashboardCsvExport(byte[] Content, string FileName);
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
