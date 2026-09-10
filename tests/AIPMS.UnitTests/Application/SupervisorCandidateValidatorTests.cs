using AIPMS.Application.Features.Supervisors.Queries;
using AIPMS.Application.Features.Supervisors.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorCandidateValidatorTests
{
    [Theory]
    [InlineData(0, 1, 20, false)]
    [InlineData(1, 0, 20, false)]
    [InlineData(1, 1, 101, false)]
    [InlineData(1, 1_000_001, 20, false)]
    [InlineData(1, 1_000_000, 100, true)]
    public void Validates_project_and_safe_paging(long project, int page, int pageSize, bool valid)
    {
        Assert.Equal(valid, new GetSupervisorCandidatesQueryValidator()
            .Validate(new GetSupervisorCandidatesQuery(project, Page: page, PageSize: pageSize)).IsValid);
    }

    [Fact]
    public void Rejects_oversized_search_filters()
    {
        var result = new GetSupervisorCandidatesQueryValidator()
            .Validate(new GetSupervisorCandidatesQuery(1, new string('x', 256), new string('x', 256)));
        Assert.Contains(result.Errors, e => e.PropertyName == "Search");
        Assert.Contains(result.Errors, e => e.PropertyName == "Expertise");
    }
}
