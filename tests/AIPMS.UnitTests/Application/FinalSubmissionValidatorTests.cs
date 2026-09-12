using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class FinalSubmissionValidatorTests
{
    [Fact]
    public void Requirements_cannot_be_empty_duplicate_negative_or_unbounded()
    {
        var validator = new ConfigureFinalRequirementsRequestValidator();
        foreach (var ids in new long[]?[] { null, [], [1, 1], [0], [-1], Enumerable.Range(1, 101).Select(i => (long)i).ToArray() })
            Assert.False(validator.Validate(new ConfigureFinalRequirementsRequest(ids!, null)).IsValid);
        Assert.True(validator.Validate(new ConfigureFinalRequirementsRequest(Enumerable.Range(1, 100).Select(i => (long)i).ToArray(), null)).IsValid);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad-token")]
    public void Submission_requires_both_confirmation_tokens(string? invalid)
    {
        var token = Guid.NewGuid().ToString();
        var validator = new SubmitFinalSubmissionRequestValidator();
        Assert.False(validator.Validate(new SubmitFinalSubmissionRequest(invalid!, token)).IsValid);
        Assert.False(validator.Validate(new SubmitFinalSubmissionRequest(token, invalid!)).IsValid);
        Assert.True(validator.Validate(new SubmitFinalSubmissionRequest(token, token)).IsValid);
    }
    [Fact]
    public void Commands_reject_null_input_and_nonpositive_projects()
    {
        Assert.False(new SubmitFinalSubmissionCommandValidator().Validate(new SubmitFinalSubmissionCommand(1, null!)).IsValid);
        Assert.False(new ConfigureFinalRequirementsCommandValidator().Validate(new ConfigureFinalRequirementsCommand(1, null!)).IsValid);
        Assert.False(new ConfigureFinalRequirementsCommandValidator().Validate(new ConfigureFinalRequirementsCommand(0, new([1], null))).IsValid);
    }
}
