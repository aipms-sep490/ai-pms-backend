using AIPMS.Application.Features.Evaluations.Models;

namespace AIPMS.Application.Features.Evaluations.Services;

public static class AcademicAuthority
{
    // Reading project assignments supports admin oversight; assignment mutations still require department scope.
    public static bool CanReadAssignments(EvaluationActor actor, EvaluationProject project) =>
        actor.IsAdmin || HasScope(actor, project);

    public static bool IsLead(EvaluationActor actor, EvaluationProject project) =>
        actor.IsStaff && actor.DepartmentId.HasValue && actor.DepartmentId == project.LeadDepartmentId
        && HasScope(actor, project);

    public static bool HasScope(EvaluationActor actor, EvaluationProject project) =>
        actor.IsStaff && actor.DepartmentId.HasValue && project.ActiveScope
        && project.ScopeProvenance == "FROZEN_REGISTRATION_SNAPSHOT"
        && project.DepartmentIds.Contains(actor.DepartmentId.Value);

    public static bool ManagesComponent(EvaluationActor actor, EvaluationProject project, long departmentId) =>
        HasScope(actor, project) && project.DepartmentIds.Contains(departmentId)
        && (IsLead(actor, project) || actor.DepartmentId == departmentId);
}
