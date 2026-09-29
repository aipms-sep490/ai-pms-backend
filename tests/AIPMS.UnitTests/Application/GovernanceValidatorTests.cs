using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Validators;
using AIPMS.Domain.Teams;

namespace AIPMS.UnitTests.Application;

public sealed class GovernanceValidatorTests
{
    [Theory]
    [InlineData("PRIMARY", null, true)]
    [InlineData("PRIMARY", 1L, false)]
    [InlineData("DISCIPLINE_MENTOR", 1L, true)]
    [InlineData("DISCIPLINE_MENTOR", null, false)]
    [InlineData("DISCIPLINE_MENTOR", 0L, false)]
    [InlineData("unknown", null, false)]
    public void Request_and_candidate_share_assignment_slot_contract(string type, long? major, bool valid)
    {
        Assert.Equal(valid, new SendSupervisorRequestCommandValidator().Validate(new SendSupervisorRequestCommand(1, 2, null, type, major)).IsValid);
        Assert.Equal(valid, new GetSupervisorCandidatesQueryValidator().Validate(new GetSupervisorCandidatesQuery(1, AssignmentType: type, MajorId: major)).IsValid);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("SINGLE_MAJOR", true)]
    [InlineData(" interdisciplinary , SINGLE_MAJOR ", true)]
    [InlineData("", false)]
    [InlineData("SINGLE_MAJOR,", false)]
    [InlineData("OTHER", false)]
    [InlineData("STUDENT_PROPOSAL", false)]
    public void Policy_modes_reject_empty_or_unknown_values(string? csv, bool valid) =>
        Assert.Equal(valid, ProjectPeriodGovernancePolicy.IsValid(csv, true));

    [Fact]
    public void Normalized_policy_order_does_not_create_a_new_version()
    {
        Assert.Equal(ProjectPeriodGovernancePolicy.Normalize("SINGLE_MAJOR,INTERDISCIPLINARY"),
            ProjectPeriodGovernancePolicy.Normalize("interdisciplinary,single_major,SINGLE_MAJOR"));
        Assert.False(ProjectPeriodGovernancePolicy.Allows("SINGLE_MAJOR", "INTERDISCIPLINARY"));
        Assert.False(ProjectPeriodGovernancePolicy.IsValid("PUBLISHED_TOPIC,BAD", false));
    }

    [Theory]
    [InlineData(0, 1, "reason", false)]
    [InlineData(1, 0, "reason", false)]
    [InlineData(1, 2, "   ", false)]
    [InlineData(1, 2, "Change of availability", true)]
    public void Replacement_requires_ids_and_reason(long assignment, long profile, string reason, bool valid) =>
        Assert.Equal(valid, new ReplaceSupervisorAssignmentCommandValidator().Validate(new ReplaceSupervisorAssignmentCommand(assignment, profile, reason)).IsValid);
}
