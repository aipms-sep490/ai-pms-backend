using System.Threading.Tasks;
using AIPMS.Application.Features.Projects.Queries;
using AIPMS.Application.Features.Projects.Validators;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class GetProjectProgressAnalysisQueryValidatorTests
{
    private readonly GetProjectProgressAnalysisQueryValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Validate_InvalidProjectId_ReturnsValidationError(long projectId)
    {
        var query = new GetProjectProgressAnalysisQuery(projectId);

        var result = await _validator.ValidateAsync(query);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(GetProjectProgressAnalysisQuery.ProjectId));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(999999)]
    public async Task Validate_ValidProjectId_PassesValidation(long projectId)
    {
        var query = new GetProjectProgressAnalysisQuery(projectId);

        var result = await _validator.ValidateAsync(query);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
