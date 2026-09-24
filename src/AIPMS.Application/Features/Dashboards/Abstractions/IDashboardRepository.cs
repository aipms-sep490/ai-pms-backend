using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.Application.Features.Dashboards.Abstractions;

public sealed record DashboardProjectFacts(long Id, string Code, string Title, string Status, long TeamId,
    long SemesterId, int PendingProgressReviews, ProjectProgressFacts Facts,
    IReadOnlyList<DashboardMajorFact>? Majors = null, DashboardSupervisorFact? Supervisor = null,
    DateTime CreatedAt = default);
public sealed record DashboardMajorFact(long ProjectId, long Id, string Code, string Name, long DepartmentId, string DepartmentName);
public sealed record DashboardSupervisorFact(long UserId, string Name);
public sealed record StudentDashboardFacts(DashboardProjectFacts? Project, int UnreadNotifications,
    int AssignedOpenTasks, int AssignedOverdueTasks, IReadOnlyList<DashboardTaskDto> TaskDeadlines,
    IReadOnlyList<DashboardMilestoneDto> MilestoneDeadlines);
public sealed record SupervisorDashboardFacts(SupervisorWorkloadDto Workload,
    IReadOnlyList<DashboardStatusCountDto> ProjectStates, int PendingProgressReviews, int OverdueTasks,
    long TotalProjects, IReadOnlyList<DashboardProjectFacts> Projects);
public sealed record SupervisorDashboardFilter(long? SemesterId, string? Status, string? Search, int Page, int PageSize);
public sealed record PortfolioDashboardFacts(long? DepartmentId, IReadOnlyList<DashboardProjectFacts> Projects);

public interface IDashboardRepository
{
    Task<T> InReadTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task RequireRoleAsync(long userId, string role, CancellationToken ct);
    Task<StudentDashboardFacts> GetStudentAsync(long userId, long? projectId, DateTime now, CancellationToken ct);
    Task<SupervisorDashboardFacts> GetSupervisorAsync(long userId, SupervisorDashboardFilter filter, DateTime now, CancellationToken ct);
    Task<PortfolioDashboardFacts> GetPortfolioAsync(long userId, bool isAdmin, DashboardPortfolioFilter filter,
        DateTime now, CancellationToken ct);
}
