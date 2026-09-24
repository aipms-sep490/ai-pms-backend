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

    [Fact]
    public void DepartmentQuery_RejectsInvalidPortfolioFilters()
    {
        var result = new GetDepartmentDashboardQueryValidator().TestValidate(
            new GetDepartmentDashboardQuery(Status: "UNKNOWN", Search: new string('x', 201), Page: 0, PageSize: 101));

        result.ShouldHaveValidationErrorFor(x => x.Status);
        result.ShouldHaveValidationErrorFor(x => x.Search);
        result.ShouldHaveValidationErrorFor(x => x.Page);
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    [Fact]
    public void AdminQuery_AllowsOptionalScopeFilters()
    {
        var result = new GetAdminDashboardQueryValidator().TestValidate(
            new GetAdminDashboardQuery(SemesterId: null, DepartmentId: null, MajorId: null,
                Status: "ACTIVE", Search: "  capstone  ", Page: 1, PageSize: 100));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ExportQuery_OnlyAllowsCsvAndValidStatus()
    {
        var result = new ExportPortfolioDashboardQueryValidator().TestValidate(
            new ExportPortfolioDashboardQuery(Format: "xlsx", Status: "NOT_A_PROJECT_STATE"));

        result.ShouldHaveValidationErrorFor(x => x.Format);
        result.ShouldHaveValidationErrorFor(x => x.Status);
    }

    [Fact]
    public async Task ExportHandler_RejectsStudentRole()
    {
        var handler = new ExportPortfolioDashboardQueryHandler(null!,
            new TestCurrentUser(10, AppRoles.Student), null!, null!, null!);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(new ExportPortfolioDashboardQuery(), default));
    }
}
