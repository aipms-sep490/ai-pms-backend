using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.WorkflowContext.DTOs;
using AIPMS.Domain.Projects;
using System.Threading.Tasks;

namespace AIPMS.Infrastructure.Services.WorkflowContext;

internal sealed partial class WorkflowContextReader
{
    public async Task<ProjectWorkflowActionsDto> GetProjectActionsAsync(long userId, IReadOnlyCollection<string> tokenRoles,
        long projectId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var actor = await ReadActorAsync(userId, tokenRoles, ct);
        var project = await projects.GetByIdAsync(projectId, ct) ?? throw new NotFoundException("Project", projectId);
        var staffDepartment = actor.Staff && actor.Academic.HasActiveDepartmentScope ? actor.Academic.Department?.Id : null;
        if (actor.User.EffectiveRoles.Count == 0 || (actor.Staff && !actor.Admin && staffDepartment is null)
            || !await projects.CanUserViewProjectAsync(projectId, userId, actor.Admin, staffDepartment, ct))
            throw new ForbiddenException("You cannot view actions for this project.");

        var team = await ReadTeamStateAsync(project.TeamId, now.UtcDateTime, ct);
        var leader = (actor.Student && team.Team.Members.Any(m => m.UserId == userId && m.IsLeader), "TEAM_LEADER_REQUIRED");
        var academicReview = await projects.GetAcademicReviewAsync(projectId, ct);
        var snapshot = academicReview.LatestSubmission;
        var departmentIds = await projects.GetProjectMajorDepartmentIdsAsync(projectId, ct);
        var hasScope = project.AcademicScope is not null;
        var reviewer = hasScope
            ? actor.Staff && actor.Academic.HasActiveDepartmentScope && snapshot?.Evidence.Scope.LeadDepartmentId == staffDepartment
            : actor.Admin || actor.Staff && staffDepartment.HasValue && departmentIds.Contains(staffDepartment.Value);
        var reviewGate = (reviewer, hasScope ? "LEAD_DEPARTMENT_REVIEWER_REQUIRED" : "REVIEWER_SCOPE_REQUIRED");
        var reviewEvidence = (!hasScope || snapshot is not null, "SUBMISSION_SNAPSHOT_REQUIRED");
        var myDecision = snapshot?.Decisions.SingleOrDefault(d => d.DepartmentId == staffDepartment);
        var hybrid = snapshot?.Evidence.Scope.ProjectMode == "INTERDISCIPLINARY";
        var departmentsApproved = !hybrid || (snapshot!.Decisions.Count == snapshot.Evidence.DepartmentIds.Count
            && snapshot.Decisions.All(d => d.Decision == "APPROVED"));
        var canTransition = Enum.TryParse<ProjectStatus>(project.Status.Replace("_", ""), true, out var status);
        bool Transition(ProjectStatus target) => canTransition && ProjectStateMachine.CanTransition(status, target);
        var proposalIssues = new List<string>();
        if (string.IsNullOrWhiteSpace(project.Title) || string.IsNullOrWhiteSpace(project.ProblemStatement)
            || string.IsNullOrWhiteSpace(project.Objectives) || string.IsNullOrWhiteSpace(project.ExpectedOutput))
            proposalIssues.Add("PROPOSAL_FIELDS_REQUIRED");
        if (project.Majors.Count == 0) proposalIssues.Add("PROJECT_MAJORS_REQUIRED");
        if (!project.Tags.Any(t => t.TagType == "DOMAIN") || !project.Tags.Any(t => t.TagType == "TECHNOLOGY") || !project.Tags.Any(t => t.TagType == "KEYWORD"))
            proposalIssues.Add("PROPOSAL_TAGS_REQUIRED");
        if (team.Team.AcademicScope is not null && !project.Majors.Select(m => m.MajorId).Order()
            .SequenceEqual(team.Team.AcademicScope.Requirements.Select(r => r.MajorId).Order()))
            proposalIssues.Add("PROJECT_MAJOR_SCOPE_MISMATCH");
        var submissionIssues = team.EligibilityIssues.Concat(proposalIssues).ToArray();
        var candidateProject = await candidateReader.GetProjectAsync(projectId, now.UtcDateTime, ct);
        var selectionPolicies = await candidateReader.GetSelectionPoliciesAsync(team.Team.SemesterId, now.UtcDateTime, ct);
        var supervisorIssues = new List<string>();
        if (candidateProject is null || candidateProject.Status != "APPROVED" || candidateProject.HasActiveAssignment)
            supervisorIssues.Add("SUPERVISOR_SELECTION_STATE_INVALID");
        if (candidateProject is null || !candidateProject.HasActiveSemester || candidateProject.DepartmentIds.Count == 0)
            supervisorIssues.Add("PROJECT_ACADEMIC_CONTEXT_INACTIVE");
        if (selectionPolicies.Count != 1 || selectionPolicies[0].MaxProjectsPerSupervisor is not > 0)
            supervisorIssues.Add("SUPERVISOR_SELECTION_POLICY_UNAVAILABLE");
        var supervisorAccess = await projectAccess.CanAccessAsync(userId, projectId, ct);
        var actions = new[]
        {
            Action("view_project"), Action("view_project_history"), Action("view_academic_review"),
            Action("edit_project_draft", leader, (project.Status is "DRAFT" or "REVISION_REQUIRED", "PROJECT_NOT_EDITABLE")),
            Action("set_project_majors", leader, (project.Status is "DRAFT" or "REVISION_REQUIRED", "PROJECT_NOT_EDITABLE")),
            WithIssues("submit_project", submissionIssues, leader, (project.Status == "DRAFT" && Transition(ProjectStatus.Submitted), "INVALID_PROJECT_STATE")),
            WithIssues("resubmit_project", submissionIssues, leader, (project.Status == "REVISION_REQUIRED" && Transition(ProjectStatus.Submitted), "INVALID_PROJECT_STATE")),
            Action("start_review", reviewGate, reviewEvidence, (Transition(ProjectStatus.UnderReview), "INVALID_PROJECT_STATE")),
            Action("request_revision", reviewGate, reviewEvidence, (Transition(ProjectStatus.RevisionRequired), "INVALID_PROJECT_STATE")),
            Action("reject_project", reviewGate, reviewEvidence, (Transition(ProjectStatus.Rejected), "INVALID_PROJECT_STATE")),
            Action("approve_project", reviewGate, reviewEvidence, (Transition(ProjectStatus.Approved), "INVALID_PROJECT_STATE"),
                (departmentsApproved, "DEPARTMENT_APPROVALS_INCOMPLETE")),
            Action("approve_department", (actor.Staff && actor.Academic.HasActiveDepartmentScope, "DEPARTMENT_STAFF_REQUIRED"),
                (project.Status == "UNDER_REVIEW", "INVALID_PROJECT_STATE"), (myDecision is not null, "NOT_REQUIRED_DEPARTMENT"),
                (myDecision?.Decision == "PENDING", "DEPARTMENT_ALREADY_DECIDED")),
            Action("reject_department", (actor.Staff && actor.Academic.HasActiveDepartmentScope, "DEPARTMENT_STAFF_REQUIRED"),
                (project.Status == "UNDER_REVIEW", "INVALID_PROJECT_STATE"), (myDecision is not null, "NOT_REQUIRED_DEPARTMENT"),
                (myDecision?.Decision == "PENDING", "DEPARTMENT_ALREADY_DECIDED")),
            WithIssues("view_supervisor_candidates", supervisorIssues,
                (supervisorAccess && (actor.Admin || actor.Academic.HasActiveDepartmentScope), "SUPERVISOR_CONTEXT_ACCESS_DENIED")),
            WithIssues("send_supervisor_request", supervisorIssues, leader, (actor.Academic.HasActiveDepartmentScope, "ACADEMIC_SCOPE_INACTIVE"))
        };
        return new(now, projectId, project.Status, project.ConcurrencyToken, snapshot?.Id, staffDepartment, actions);
    }
}
