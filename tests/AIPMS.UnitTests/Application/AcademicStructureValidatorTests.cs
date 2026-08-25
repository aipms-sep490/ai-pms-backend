using AIPMS.Application.Features.Academic.Commands;
using AIPMS.Application.Features.Academic.Queries;
using AIPMS.Application.Features.Academic.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class AcademicStructureValidatorTests
{
    [Fact]
    public void CreateMajor_InvalidDepartmentCodeAndName_ReturnsValidationErrors()
    {
        var validator = new CreateMajorCommandValidator();

        var result = validator.Validate(
            new CreateMajorCommand(0, "invalid code!", string.Empty, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "DepartmentId");
        Assert.Contains(result.Errors, error => error.PropertyName == "Code");
        Assert.Contains(result.Errors, error => error.PropertyName == "Name");
    }

    [Fact]
    public void GetOrganizations_InvalidPaging_ReturnsValidationErrors()
    {
        var validator = new GetOrganizationsQueryValidator();

        var result = validator.Validate(
            new GetOrganizationsQuery(null, null, 0, 101));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Page");
        Assert.Contains(result.Errors, error => error.PropertyName == "PageSize");
    }
}
