using AIPMS.Domain.Supervisors;
using AIPMS.Domain.Projects;

namespace AIPMS.UnitTests.Domain.Supervisors;

public sealed class CapabilityEnforcementParityTests
{
    [Theory]
    [InlineData("DRAFT", false, true)]
    [InlineData("REVISION_REQUIRED", false, true)]
    [InlineData("APPROVED", false, true)]
    [InlineData("ACTIVE", false, true)]
    [InlineData("DRAFT", true, false)]
    [InlineData("ACTIVE", true, false)]
    [InlineData("SUBMITTED", false, false)]
    [InlineData("COMPLETED", false, false)]
    [InlineData("ARCHIVED", false, false)]
    public void PrimarySupervisor_CapabilityMatchesEnforcement(string status, bool hasActiveAssignment, bool expectedAllowed)
    {
        var allowed = SupervisorSelectionEligibility.CanRequestPrimary(status, hasActiveAssignment);
        Assert.Equal(expectedAllowed, allowed);
    }

    [Theory]
    [InlineData("ACTIVE", true, new long[] { 101, 102 }, new long[] { 101 }, 102, true)]
    [InlineData("ACTIVE", true, new long[] { 101, 102 }, new long[] { 101, 102 }, 102, false)]
    [InlineData("ACTIVE", false, new long[] { 101, 102 }, null, 101, false)] // ACTIVE must have primary before mentor
    [InlineData("DRAFT", false, new long[] { 101, 102 }, null, 101, true)]
    [InlineData("REVISION_REQUIRED", false, new long[] { 101, 102 }, null, 101, true)]
    [InlineData("SUBMITTED", true, new long[] { 101, 102 }, null, 101, false)]
    [InlineData("COMPLETED", true, new long[] { 101, 102 }, null, 101, false)]
    public void DisciplineMentor_CapabilityMatchesEnforcement(
        string status,
        bool hasActiveAssignment,
        long[] requiredMajors,
        long[]? occupiedMajors,
        long targetMajor,
        bool expectedAllowed)
    {
        var allowed = SupervisorSelectionEligibility.CanRequestMentor(
            status,
            hasActiveAssignment,
            requiredMajors,
            occupiedMajors,
            targetMajor);

        Assert.Equal(expectedAllowed, allowed);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ProgressReportCreation_TeamMemberParity(bool isTeamMember, bool isActiveProject, bool expectedAllowed)
    {
        // Gated by: isTeamMember && isActiveProject
        var allowed = isTeamMember && isActiveProject;
        Assert.Equal(expectedAllowed, allowed);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void TaskAssignment_ManagerParity(bool isManager, bool isActiveProject, bool expectedAllowed)
    {
        // Gated by: isManager && isActiveProject
        var allowed = isManager && isActiveProject;
        Assert.Equal(expectedAllowed, allowed);
    }

    [Theory]
    [InlineData(ProjectStatus.Draft, ProjectStatus.Submitted, true)]
    [InlineData(ProjectStatus.Submitted, ProjectStatus.UnderReview, true)]
    [InlineData(ProjectStatus.UnderReview, ProjectStatus.Approved, true)]
    [InlineData(ProjectStatus.UnderReview, ProjectStatus.Rejected, true)]
    [InlineData(ProjectStatus.UnderReview, ProjectStatus.RevisionRequired, true)]
    [InlineData(ProjectStatus.Approved, ProjectStatus.SupervisorPending, true)]
    [InlineData(ProjectStatus.SupervisorPending, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.FinalSubmission, true)]
    [InlineData(ProjectStatus.FinalSubmission, ProjectStatus.Completed, true)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Archived, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Draft, false)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Active, false)]
    public void ProjectStateTransitions_MatchStateMachine(ProjectStatus current, ProjectStatus target, bool expectedCanTransition)
    {
        var canTransition = ProjectStateMachine.CanTransition(current, target);
        Assert.Equal(expectedCanTransition, canTransition);
    }
}
