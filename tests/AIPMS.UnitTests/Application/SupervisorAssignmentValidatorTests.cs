using AIPMS.Application.Features.Supervisors.Commands;
using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Validators;
using FluentValidation.TestHelper;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorAssignmentValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void End_requires_a_reason(string? reason) =>
        new EndSupervisorAssignmentCommandValidator().TestValidate(new EndSupervisorAssignmentCommand(1, reason!))
            .ShouldHaveValidationErrorFor(r => r.Reason);

    [Fact]
    public void End_validates_identifier_and_reason_length()
    {
        var validator = new EndSupervisorAssignmentCommandValidator();
        validator.TestValidate(new EndSupervisorAssignmentCommand(0, new string('a', 2001))).ShouldHaveValidationErrorFor(r => r.AssignmentId);
        validator.TestValidate(new EndSupervisorAssignmentCommand(1, new string('a', 2001))).ShouldHaveValidationErrorFor(r => r.Reason);
        validator.TestValidate(new EndSupervisorAssignmentCommand(1, new string('a', 2000))).ShouldNotHaveAnyValidationErrors();
        new GetSupervisorAssignmentQueryValidator().TestValidate(new GetSupervisorAssignmentQuery(-1)).ShouldHaveValidationErrorFor(r => r.AssignmentId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ACTIVE")]
    [InlineData("ENDED")]
    public void Assignment_filters_allow_only_defined_statuses(string? status)
    {
        var validator = new GetSupervisorAssignmentsQueryValidator();
        validator.TestValidate(new GetSupervisorAssignmentsQuery(null, status)).ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new GetSupervisorAssignmentsQuery(1, status, 1_000_000, 100)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Assignment_filters_reject_invalid_paging_and_scope()
    {
        var result = new GetSupervisorAssignmentsQueryValidator().TestValidate(new GetSupervisorAssignmentsQuery(0, "ended", 1_000_001, 101));
        result.ShouldHaveValidationErrorFor(r => r.ProjectId);
        result.ShouldHaveValidationErrorFor(r => r.Status);
        result.ShouldHaveValidationErrorFor(r => r.Page);
        result.ShouldHaveValidationErrorFor(r => r.PageSize);
    }
}
