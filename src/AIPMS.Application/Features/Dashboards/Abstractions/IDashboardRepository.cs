using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Projects.Models;

namespace AIPMS.Application.Features.Dashboards.Abstractions;

public sealed record DashboardProjectFacts(long Id, string Code, string Title, string Status, long TeamId,
    long SemesterId, int PendingProgressReviews, ProjectProgressFacts Facts);
public sealed record StudentDashboardFacts(DashboardProjectFacts? Project, int UnreadNotifications,
    int AssignedOpenTasks, int AssignedOverdueTasks, IReadOnlyList<DashboardTaskDto> TaskDeadlines,
    IReadOnlyList<DashboardMilestoneDto> MilestoneDeadlines);
public sealed record SupervisorDashboardFacts(SupervisorWorkloadDto Workload,
    IReadOnlyList<DashboardStatusCountDto> ProjectStates, int PendingProgressReviews, int OverdueTasks,
    long TotalProjects, IReadOnlyList<DashboardProjectFacts> Projects);
public sealed record SupervisorDashboardFilter(long? SemesterId, string? Status, string? Search, int Page, int PageSize);

public interface IDashboardRepository
{
    Task<T> InReadTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
    Task RequireRoleAsync(long userId, string role, CancellationToken ct);
    Task<StudentDashboardFacts> GetStudentAsync(long userId, long? projectId, DateTime now, CancellationToken ct);
    Task<SupervisorDashboardFacts> GetSupervisorAsync(long userId, SupervisorDashboardFilter filter, DateTime now, CancellationToken ct);
}
