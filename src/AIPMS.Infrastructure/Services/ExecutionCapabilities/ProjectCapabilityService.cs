using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Services;
using AIPMS.Application.Features.ExecutionCapabilities.Abstractions;
using AIPMS.Application.Features.ExecutionCapabilities.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Services.ExecutionCapabilities;

internal sealed class ProjectCapabilityService(AipmsDbContext db, ICurrentUser current,
    IExecutionCapabilityService execution, IEvaluationDraftRepository evaluations,
    IEvaluationSchemeService schemes, TimeProvider clock) : IProjectCapabilityService
{
    public async Task<ProjectCapabilitiesDto> GetAsync(long projectId, CancellationToken ct)
    {
        var actorId = current.UserId ?? throw new UnauthorizedException();
        var basics = await execution.GetProjectAsync(projectId, ct);
        var actor = await evaluations.GetActorAsync(actorId, ct) ?? throw new ForbiddenException();
        var project = await evaluations.GetProjectAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var lead = AcademicAuthority.IsLead(actor, project);
        var scoped = AcademicAuthority.HasScope(actor, project);
        var now = clock.GetUtcNow().UtcDateTime;
        var periods = await db.ProjectPeriods.Where(p => p.AcademicSemesterId == project.SemesterId
            && p.PeriodType == "EVALUATION" && p.Status == "ACTIVE" && p.StartAt <= now && now < p.EndAt)
            .Select(p => p.Id).Take(2).ToArrayAsync(ct);
        var open = periods.Length == 1 && (await evaluations.GetPeriodAsync(periods[0], now, ct))?.IsOpen == true;
        var package = await evaluations.HasLockedSubmissionAsync(projectId, ct);
        var scheme = await db.Set<EvaluationScheme>().AsNoTracking().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Status == "PUBLISHED").ThenByDescending(s => s.Version)
            .Select(s => new { s.Id, s.Status, s.ConcurrencyToken, s.PolicyVersionId, s.ProjectPeriodId })
            .FirstOrDefaultAsync(ct);
        var writable = project.Status == "FINAL_SUBMISSION" && project.ActiveScope && package && open;
        var reason = !project.ActiveScope ? "ACADEMIC_SCOPE_UNKNOWN_OR_INACTIVE" : project.Status != "FINAL_SUBMISSION"
            ? "PROJECT_NOT_FINAL_SUBMISSION" : !package ? "FINAL_PACKAGE_REQUIRED" : !open ? "EVALUATION_WINDOW_CLOSED"
            : "LEAD_DEPARTMENT_REQUIRED";
        var actions = basics.Actions.ToList();
        actions.Add(Action("manage_evaluation_scheme", lead && writable, reason));
        actions.Add(Action("manage_evaluators", scoped && writable && scheme?.Status == "PUBLISHED"
            && periods.Contains(scheme.ProjectPeriodId), !scoped ? "ACADEMIC_COMPONENT_SCOPE_REQUIRED"
                : !writable ? reason : "PUBLISHED_SCHEME_REQUIRED"));
        var publish = false;
        IReadOnlyList<string> blockers = [lead ? "RESULT_NOT_READY" : "LEAD_DEPARTMENT_REQUIRED_FOR_PUBLICATION"];
        if (lead && scheme?.Status == "PUBLISHED")
        {
            try
            {
                var preview = await schemes.PreviewAsync(projectId, null, ct);
                publish = preview.CanPublish;
                blockers = preview.Blockers;
            }
            catch (ConflictException) { blockers = ["RESULT_NOT_READY"]; }
            catch (ForbiddenException) { blockers = ["RESULT_PUBLICATION_FORBIDDEN"]; }
        }
        actions.Add(new("publish_project_result", publish, publish ? [] : blockers));
        var snapshot = await db.Set<ProjectRegistrationSnapshot>().Where(s => s.ProjectId == projectId)
            .OrderByDescending(s => s.Id).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct);
        return new(projectId, basics.ProjectStatus, basics.ConcurrencyToken, project.ScopeProvenance,
            project.LeadDepartmentId, lead ? project.DepartmentIds : scoped ? [actor.DepartmentId!.Value] : [],
            snapshot, scheme?.Id, scheme?.ConcurrencyToken.ToString("N"), scheme?.PolicyVersionId, actions);
    }

    private static ExecutionActionDto Action(string code, bool allowed, string reason) => new(code, allowed, allowed ? [] : [reason]);
}
