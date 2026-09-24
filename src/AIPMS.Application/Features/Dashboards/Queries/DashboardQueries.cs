using System.Globalization;
using System.Text;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.AI;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Contributions.Abstractions;
using AIPMS.Application.Features.Contributions.DTOs;
using AIPMS.Application.Features.Dashboards.Abstractions;
using AIPMS.Application.Features.Dashboards.DTOs;
using AIPMS.Application.Features.Projects.DTOs;
using AIPMS.Application.Features.WorkflowContext.Abstractions;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Domain.Exceptions;
using MediatR;

namespace AIPMS.Application.Features.Dashboards.Queries;

public sealed record GetStudentDashboardQuery(long? SemesterId = null) : IRequest<StudentDashboardDto>;
public sealed record GetSupervisorDashboardQuery(long? SemesterId = null, string? Status = null,
    string? Search = null, int Page = 1, int PageSize = 20) : IRequest<SupervisorDashboardDto>;
public sealed record GetDepartmentDashboardQuery(long? SemesterId = null, long? MajorId = null,
    string? Status = null, string? Search = null, int Page = 1, int PageSize = 20) : IRequest<PortfolioDashboardDto>;
public sealed record GetAdminDashboardQuery(long? SemesterId = null, long? DepartmentId = null, long? MajorId = null,
    string? Status = null, string? Search = null, int Page = 1, int PageSize = 20) : IRequest<PortfolioDashboardDto>;
public sealed record ExportPortfolioDashboardQuery(long? SemesterId = null, long? DepartmentId = null, long? MajorId = null,
    string? Status = null, string? Search = null, string? Format = "csv") : IRequest<DashboardCsvExport>;

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

    private static DashboardProjectDto PortfolioProject(DashboardProjectFacts project, ProjectProgressAnalysisDto result,
        DateTime now, CancellationToken ct) => new(project.Id, project.Code, project.Title, project.Status,
        project.TeamId, project.SemesterId, project.PendingProgressReviews, result,
        project.Majors?.OrderBy(m => m.DepartmentId).Select(m => (long?)m.DepartmentId).FirstOrDefault(),
        project.Majors?.OrderBy(m => m.DepartmentId).Select(m => m.DepartmentName).FirstOrDefault(),
        project.Majors?.Select(m => new DashboardMajorDto(m.Id, m.Code, m.Name)).ToArray(),
        project.Supervisor is null ? null : new DashboardSupervisorDto(project.Supervisor.UserId, project.Supervisor.Name));

    public static PortfolioDashboardDto Portfolio(PortfolioDashboardFacts facts, IProgressAnalysisService analysis,
        DateTimeOffset now, DashboardPortfolioFilter filter, CancellationToken ct)
    {
        var analyzed = facts.Projects
            .Select(project => (Project: project, Analysis: analysis.Analyze(project.Facts, now.UtcDateTime, ct)))
            .ToArray();
        var states = analyzed.GroupBy(x => x.Project.Status).OrderBy(g => g.Key)
            .Select(g => new DashboardStatusCountDto(g.Key, checked((int)g.LongCount()))).ToArray();
        var risks = analyzed.GroupBy(x => x.Analysis.RiskLevel).OrderBy(g => g.Key)
            .Select(g => new DashboardStatusCountDto(g.Key, checked((int)g.LongCount()))).ToArray();
        var majors = facts.Projects.SelectMany(p => p.Majors ?? [])
            .GroupBy(m => new { m.Id, m.Code, m.Name }).OrderBy(g => g.Key.Code)
            .Select(g => new DashboardMajorSummaryDto(g.Key.Id, g.Key.Code, g.Key.Name,
                g.Select(m => m.ProjectId).Distinct().LongCount())).ToArray();
        var supervisors = facts.Projects.GroupBy(p => p.Supervisor)
            .OrderBy(g => g.Key?.Name ?? "")
            .Select(g => new DashboardSupervisorSummaryDto(g.Key?.UserId, g.Key?.Name, g.LongCount(),
                g.Sum(x => x.Facts.Tasks.Count(t => t.DueAt < now.UtcDateTime && t.Status is "TODO" or "IN_PROGRESS" or "BLOCKED" or "IN_REVIEW")),
                g.Sum(x => x.PendingProgressReviews))).ToArray();
        var page = analyzed
            .OrderByDescending(x => x.Project.CreatedAt)
            .ThenByDescending(x => x.Project.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(x => PortfolioProject(x.Project, x.Analysis, now.UtcDateTime, ct)).ToArray();
        return new PortfolioDashboardDto(now, facts.DepartmentId,
            new(facts.Projects.Count, states, majors, supervisors, risks),
            new(page, filter.Page, filter.PageSize, facts.Projects.Count));
    }
}

internal static class DashboardCsvWriter
{
    public static byte[] Write(IEnumerable<(DashboardProjectFacts Project, ProjectProgressAnalysisDto Analysis)> rows, DateTime now)
    {
        var builder = new StringBuilder();
        AppendRow(builder, "projectId", "code", "title", "status", "semesterId", "majorCodes", "supervisor", "riskLevel", "riskScore", "progressPercentage", "totalTasks", "doneTasks",
            "blockedTasks", "overdueTasks", "generatedAtUtc");
        foreach (var (project, analysis) in rows.OrderByDescending(x => x.Project.CreatedAt).ThenByDescending(x => x.Project.Id))
        {
            AppendRow(builder, project.Id.ToString(CultureInfo.InvariantCulture), project.Code, project.Title, project.Status,
                project.SemesterId.ToString(CultureInfo.InvariantCulture),
                string.Join(";", (project.Majors ?? []).Select(m => m.Code)), project.Supervisor?.Name ?? "",
                analysis.RiskLevel, analysis.RiskScore?.ToString(CultureInfo.InvariantCulture) ?? "",
                analysis.ProgressSummary.ProgressPercentage.ToString(CultureInfo.InvariantCulture),
                analysis.ProgressSummary.TotalTasks.ToString(CultureInfo.InvariantCulture),
                analysis.ProgressSummary.DoneTasks.ToString(CultureInfo.InvariantCulture),
                analysis.ProgressSummary.BlockedTasks.ToString(CultureInfo.InvariantCulture),
                analysis.ProgressSummary.OverdueTasks.ToString(CultureInfo.InvariantCulture),
                now.ToString("O", CultureInfo.InvariantCulture));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }

    private static void AppendRow(StringBuilder builder, params string[] values)
    {
        // RFC 4180 specifies CRLF row endings; keep exports identical on every host OS.
        builder.Append(string.Join(",", values.Select(Escape))).Append("\r\n");
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
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

public sealed class GetDepartmentDashboardQueryHandler(IDashboardRepository repository, ICurrentUser currentUser,
    IProgressAnalysisService analysis, TimeProvider clock)
    : IRequestHandler<GetDepartmentDashboardQuery, PortfolioDashboardDto>
{
    public Task<PortfolioDashboardDto> Handle(GetDepartmentDashboardQuery request, CancellationToken ct)
    {
        var actor = DashboardMapping.Actor(currentUser, AppRoles.DepartmentStaff);
        return repository.InReadTransactionAsync(async token =>
        {
            await repository.RequireRoleAsync(actor, AppRoles.DepartmentStaff, token);
            var now = clock.GetUtcNow();
            var facts = await repository.GetPortfolioAsync(actor, false,
                new(request.SemesterId, null, request.MajorId, request.Status, request.Search?.Trim(), request.Page, request.PageSize),
                now.UtcDateTime, token);
            return DashboardMapping.Portfolio(facts, analysis, now, new(request.SemesterId, null, request.MajorId,
                request.Status, request.Search?.Trim(), request.Page, request.PageSize), token);
        }, ct);
    }
}

public sealed class GetAdminDashboardQueryHandler(IDashboardRepository repository, ICurrentUser currentUser,
    IProgressAnalysisService analysis, TimeProvider clock)
    : IRequestHandler<GetAdminDashboardQuery, PortfolioDashboardDto>
{
    public Task<PortfolioDashboardDto> Handle(GetAdminDashboardQuery request, CancellationToken ct)
    {
        var actor = DashboardMapping.Actor(currentUser, AppRoles.Admin);
        return repository.InReadTransactionAsync(async token =>
        {
            await repository.RequireRoleAsync(actor, AppRoles.Admin, token);
            var now = clock.GetUtcNow();
            var filter = new DashboardPortfolioFilter(request.SemesterId, request.DepartmentId, request.MajorId,
                request.Status, request.Search?.Trim(), request.Page, request.PageSize);
            var facts = await repository.GetPortfolioAsync(actor, true, filter, now.UtcDateTime, token);
            return DashboardMapping.Portfolio(facts, analysis, now, filter, token);
        }, ct);
    }
}

public sealed class ExportPortfolioDashboardQueryHandler(IDashboardRepository repository, ICurrentUser currentUser,
    IProgressAnalysisService analysis, IAuditTrail audit, TimeProvider clock)
    : IRequestHandler<ExportPortfolioDashboardQuery, DashboardCsvExport>
{
    public Task<DashboardCsvExport> Handle(ExportPortfolioDashboardQuery request, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? throw new UnauthorizedException();
        var isAdmin = currentUser.Roles.Contains(AppRoles.Admin);
        if (!isAdmin && !currentUser.Roles.Contains(AppRoles.DepartmentStaff))
            throw new ForbiddenException("Only department staff or administrators can export dashboard data.");
        if (!isAdmin && request.DepartmentId.HasValue)
            throw new ForbiddenException("Department filtering is only available to administrators.");
        return repository.InReadTransactionAsync(async token =>
        {
            await repository.RequireRoleAsync(actor, isAdmin ? AppRoles.Admin : AppRoles.DepartmentStaff, token);
            if (request.Format is not null && !string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("Only CSV export is supported.");
            var filter = new DashboardPortfolioFilter(request.SemesterId, isAdmin ? request.DepartmentId : null,
                request.MajorId, request.Status, request.Search?.Trim(), 1, 10000);
            var now = clock.GetUtcNow();
            var facts = await repository.GetPortfolioAsync(actor, isAdmin, filter, now.UtcDateTime, token);
            var analyzed = facts.Projects.Select(p => (Project: p, Analysis: analysis.Analyze(p.Facts, now.UtcDateTime, token))).ToArray();
            var content = DashboardCsvWriter.Write(analyzed, now.UtcDateTime);
            await audit.RecordAsync(new AuditEntry(actor, "DASHBOARD_EXPORTED", "DASHBOARD", null,
                new Dictionary<string, object?>
                {
                    ["role"] = isAdmin ? AppRoles.Admin : AppRoles.DepartmentStaff,
                    ["format"] = "csv", ["rowCount"] = facts.Projects.Count,
                    ["semesterId"] = filter.SemesterId, ["departmentId"] = filter.DepartmentId,
                    ["majorId"] = filter.MajorId, ["status"] = filter.Status, ["search"] = filter.Search
                }), token);
            var scope = isAdmin ? "admin" : "department";
            return new DashboardCsvExport(content, $"dashboard-{scope}-{now:yyyyMMddHHmmss}.csv");
        }, ct);
    }
}
