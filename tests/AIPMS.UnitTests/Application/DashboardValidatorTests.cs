using AIPMS.Application.Common.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Dashboards.Queries;
using AIPMS.Application.Features.Dashboards.Validators;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class DashboardValidatorTests
{
    [Fact]
    public void SupervisorQuery_RejectsInvalidPagingAndStatus()
    {
        var result = new GetSupervisorDashboardQueryValidator().TestValidate(
            new GetSupervisorDashboardQuery(Status: "NOT_A_PROJECT_STATE", Page: 0, PageSize: 51));

        result.ShouldHaveValidationErrorFor(x => x.Page);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public void StudentQuery_RejectsNonPositiveSemester()
    {
        var result = new GetStudentDashboardQueryValidator().TestValidate(new GetStudentDashboardQuery(0));

        result.ShouldHaveValidationErrorFor(x => x.SemesterId);
    }

    [Fact]
    public async Task SupervisorHandler_RequiresLecturerRole()
    {
        var handler = new GetSupervisorDashboardQueryHandler(null!, new TestCurrentUser(10, AppRoles.Student), null!, null!);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new GetSupervisorDashboardQuery(), default));
    }
}
