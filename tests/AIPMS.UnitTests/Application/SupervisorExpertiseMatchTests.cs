using AIPMS.Application.Features.Supervisors.Services;

namespace AIPMS.UnitTests.Application;

public sealed class SupervisorExpertiseMatchTests
{
    [Theory]
    [InlineData(" se ", "SE", "Software Engineering", true)]
    [InlineData("software engineering", "SE", "Software Engineering", true)]
    [InlineData("Software", "SE", "Software Engineering", false)]
    [InlineData("AI", "SE", "Software Engineering", false)]
    [InlineData("", "SE", "Software Engineering", false)]
    [InlineData("Design", "MKT", "Marketing", false)]
    public void Only_normalized_exact_code_or_name_matches(string expertise, string code, string name, bool expected) =>
        Assert.Equal(expected, SupervisorExpertiseMatch.Matches(expertise, code, name));
}
