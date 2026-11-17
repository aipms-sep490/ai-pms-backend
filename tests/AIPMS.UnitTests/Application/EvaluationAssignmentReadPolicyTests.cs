using AIPMS.Application.Features.Evaluations.Models;
using AIPMS.Application.Features.Evaluations.Services;

namespace AIPMS.UnitTests.Application;

public sealed class EvaluationAssignmentReadPolicyTests
{
    private static readonly EvaluationProject Project = new(100, 1, 1, "FINAL_SUBMISSION", true,
        [1, 2], 1, "FROZEN_REGISTRATION_SNAPSHOT");

    [Fact]
    public void Admin_can_read_assignments_without_receiving_department_management_scope()
    {
        var admin = new EvaluationActor(10, null, true, false, false);

        Assert.True(AcademicAuthority.CanReadAssignments(admin, Project));
        Assert.False(AcademicAuthority.HasScope(admin, Project));
    }

    [Fact]
    public void Department_staff_can_read_only_with_frozen_project_scope()
    {
        var participatingStaff = new EvaluationActor(11, 2, false, true, false);
        var outsider = new EvaluationActor(12, 3, false, true, false);
        var legacyProject = Project with { ScopeProvenance = "LEGACY_UNKNOWN" };

        Assert.True(AcademicAuthority.CanReadAssignments(participatingStaff, Project));
        Assert.False(AcademicAuthority.CanReadAssignments(outsider, Project));
        Assert.False(AcademicAuthority.CanReadAssignments(participatingStaff, legacyProject));
    }

    [Fact]
    public void Lecturer_without_staff_scope_cannot_list_project_assignments()
    {
        var lecturer = new EvaluationActor(13, 2, false, false, true);

        Assert.False(AcademicAuthority.CanReadAssignments(lecturer, Project));
    }
}
