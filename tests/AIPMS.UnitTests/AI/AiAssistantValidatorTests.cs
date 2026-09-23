using AIPMS.Application.Features.AiAssistant.Queries;
using AIPMS.Application.Features.AiAssistant.Validators;
using Xunit;

namespace AIPMS.UnitTests.AI;

public sealed class AiAssistantValidatorTests
{
    [Fact]
    public void SummarizeProgressReportQueryValidator_WhenValid_PassesValidation()
    {
        var validator = new SummarizeProgressReportQueryValidator();
        var query = new SummarizeProgressReportQuery(101, 1);

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(101, 0)]
    [InlineData(101, -5)]
    public void SummarizeProgressReportQueryValidator_WhenInvalidIds_FailsValidation(long projectId, long reportId)
    {
        var validator = new SummarizeProgressReportQueryValidator();
        var query = new SummarizeProgressReportQuery(projectId, reportId);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AskProjectAssistantQueryValidator_WhenValid_PassesValidation()
    {
        var validator = new AskProjectAssistantQueryValidator();
        var query = new AskProjectAssistantQuery(101, "What is the project milestone status?");

        var result = validator.Validate(query);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0, "Valid query")]
    [InlineData(-5, "Valid query")]
    [InlineData(101, "")]
    [InlineData(101, "   ")]
    public void AskProjectAssistantQueryValidator_WhenInvalidInputs_FailsValidation(long projectId, string queryText)
    {
        var validator = new AskProjectAssistantQueryValidator();
        var query = new AskProjectAssistantQuery(projectId, queryText);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AskProjectAssistantQueryValidator_WhenQueryExceeds1000Chars_FailsValidation()
    {
        var validator = new AskProjectAssistantQueryValidator();
        var longQuery = new string('A', 1001);
        var query = new AskProjectAssistantQuery(101, longQuery);

        var result = validator.Validate(query);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Query");
    }
}
