using AIPMS.Application.Features.Evaluations.Queries;
using AIPMS.Application.Features.Evaluations.Validators;
using AIPMS.Application.Features.Projects.Services;

namespace AIPMS.UnitTests.Application;

public sealed class EligibleEvaluatorValidatorTests
{
    [Theory]
    [InlineData(1, 1, 1, 20, true)]
    [InlineData(0, 1, 1, 20, false)]
    [InlineData(1, 0, 1, 20, false)]
    [InlineData(1, 1, 0, 20, false)]
    [InlineData(1, 1, 1000001, 20, false)]
    [InlineData(1, 1, 1, 101, false)]
    [InlineData(1, 1, 1, 0, false)]
    public void Discovery_bounds(long project, long period, int page, int size, bool valid) =>
        Assert.Equal(valid, new GetEligibleEvaluatorsQueryValidator().Validate(new GetEligibleEvaluatorsQuery(project, period, page, size)).IsValid);

    [Theory]
    [InlineData(true, false, null, true)]
    [InlineData(false, true, 7L, true)]
    [InlineData(false, true, 8L, false)]
    [InlineData(false, true, null, false)]
    [InlineData(false, false, 7L, false)]
    public void Archive_requires_administrative_scope(bool admin, bool staff, long? department, bool allowed) =>
        Assert.Equal(allowed, ProjectArchivePolicy.HasScope(admin, staff, department, [7]));
}
