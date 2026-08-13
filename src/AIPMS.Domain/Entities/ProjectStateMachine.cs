using AIPMS.Domain.Enums;

namespace AIPMS.Domain.Entities;

public static class ProjectStateMachine
{
    private static readonly IReadOnlyDictionary<ProjectStatus, ProjectStatus[]> AllowedTransitions =
        new Dictionary<ProjectStatus, ProjectStatus[]>
        {
            [ProjectStatus.Draft] = [ProjectStatus.Submitted],
            [ProjectStatus.Submitted] = [ProjectStatus.UnderReview],
            [ProjectStatus.UnderReview] =
                [ProjectStatus.RevisionRequired, ProjectStatus.Rejected, ProjectStatus.Approved],
            [ProjectStatus.RevisionRequired] = [ProjectStatus.Submitted],
            [ProjectStatus.Rejected] = [],
            [ProjectStatus.Approved] = [ProjectStatus.SupervisorPending],
            [ProjectStatus.SupervisorPending] = [ProjectStatus.Active],
            [ProjectStatus.Active] = [ProjectStatus.FinalSubmission],
            [ProjectStatus.FinalSubmission] = [ProjectStatus.Completed],
            [ProjectStatus.Completed] = [ProjectStatus.Archived],
            [ProjectStatus.Archived] = []
        };

    public static IReadOnlyList<ProjectStatus> GetAllowedTransitions(ProjectStatus currentStatus) =>
        [.. AllowedTransitions[currentStatus]];

    public static bool CanTransition(ProjectStatus currentStatus, ProjectStatus nextStatus) =>
        AllowedTransitions[currentStatus].Contains(nextStatus);
}
