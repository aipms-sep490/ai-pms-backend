using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.WorkflowContext.Abstractions;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Dashboards.Queries;

public sealed record GetStudentDashboardQuery(long? SemesterId = null) : IRequest<StudentDashboardDto>;
public sealed record GetSupervisorDashboardQuery(long? SemesterId = null, string? Status = null,
    string? Search = null, int Page = 1, int PageSize = 20) : IRequest<SupervisorDashboardDto>;

internal static class DashboardMapping
{
    public static long Actor(ICurrentUser user, string role)
    {
        if (!user.IsAuthenticated || user.UserId is null) throw new UnauthorizedException();
        if (!user.Roles.Contains(role)) throw new ForbiddenException("This dashboard requires the appropriate role.");
        return user.UserId.Value;
    }

    public static DashboardProjectDto Project(DashboardProjectFacts project, IProgressAnalysisService analysis,
        DateTime now, CancellationToken ct) => new(project.Id, project.Code, project.Title, project.Status,
        project.TeamId, project.SemesterId, project.PendingProgressReviews, analysis.Analyze(project.Facts, now, ct));
}

public sealed class GetStudentDashboardQueryHandler(IDashboardRepository repository, ICurrentUser currentUser,
    IWorkflowContextReader workflow, IContributionRepository contributions, IProgressAnalysisService analysis,
    TimeProvider clock) : IRequestHandler<GetStudentDashboardQuery, StudentDashboardDto>
{
    public Task<StudentDashboardDto> Handle(GetStudentDashboardQuery request, CancellationToken ct)
    {
        var actor = DashboardMapping.Actor(currentUser, AppRoles.Student);
        return repository.InReadTransactionAsync(async token =>
        {
            await repository.RequireRoleAsync(actor, AppRoles.Student, token);
            var now = clock.GetUtcNow();
            var context = await workflow.GetCurrentAsync(actor, currentUser.Roles, request.SemesterId, token);
            var facts = await repository.GetStudentAsync(actor, context.CurrentTeam?.ProjectId, now.UtcDateTime, token);
            IReadOnlyList<WorkflowActionDto> actions = context.CurrentTeam is null ? context.Actions
                : facts.Project is null
                    ? (await workflow.GetTeamActionsAsync(actor, currentUser.Roles, context.CurrentTeam.Id, token)).Actions
                    : (await workflow.GetProjectActionsAsync(actor, currentUser.Roles, facts.Project.Id, token)).Actions;
            ContributionSummaryDto? contribution = null;
            if (facts.Project is not null)
            {
                try { contribution = await contributions.GetSummaryAsync(facts.Project.Id, false, token); }
                catch (NotFoundException) when (facts.Project.Status == "ARCHIVED")
                {
                    // Legacy archives may have no frozen snapshot; never substitute live evidence.
                }
            }
            return new StudentDashboardDto(now, context, actions,
                facts.Project is null ? null : DashboardMapping.Project(facts.Project, analysis, now.UtcDateTime, token),
                facts.UnreadNotifications, facts.AssignedOpenTasks, facts.AssignedOverdueTasks,
                facts.TaskDeadlines, facts.MilestoneDeadlines,
                contribution?.Members.SingleOrDefault(m => m.UserId == actor), contribution?.DataStatus ?? "UNAVAILABLE");
        }, ct);
    }
}

public sealed class GetSupervisorDashboardQueryHandler(IDashboardRepository repository, ICurrentUser currentUser,
    IProgressAnalysisService analysis, TimeProvider clock)
    : IRequestHandler<GetSupervisorDashboardQuery, SupervisorDashboardDto>
{
    public Task<SupervisorDashboardDto> Handle(GetSupervisorDashboardQuery request, CancellationToken ct)
    {
        var actor = DashboardMapping.Actor(currentUser, AppRoles.Lecturer);
        return repository.InReadTransactionAsync(async token =>
        {
            await repository.RequireRoleAsync(actor, AppRoles.Lecturer, token);
            var now = clock.GetUtcNow();
            var facts = await repository.GetSupervisorAsync(actor,
                new(request.SemesterId, request.Status, request.Search?.Trim(), request.Page, request.PageSize), now.UtcDateTime, token);
            return new SupervisorDashboardDto(now, facts.Workload, facts.ProjectStates, facts.PendingProgressReviews,
                facts.OverdueTasks, new(facts.Projects.Select(p => DashboardMapping.Project(p, analysis, now.UtcDateTime, token)).ToArray(),
                    request.Page, request.PageSize, facts.TotalProjects));
        }, ct);
    }
}
