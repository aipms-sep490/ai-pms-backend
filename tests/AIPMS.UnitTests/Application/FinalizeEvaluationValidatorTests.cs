using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using AIPMS.Application.Features.Evaluations.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class FinalizeEvaluationValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stale")]
    public void Confirmation_needs_valid_token(string? token) => Assert.False(
        new FinalizeEvaluationRequestValidator().Validate(new FinalizeEvaluationRequest(token!)).IsValid);

    [Fact]
    public void Valid_token_and_positive_id_are_required()
    {
        var validator = new FinalizeEvaluationCommandValidator();
        Assert.True(validator.Validate(new FinalizeEvaluationCommand(1, new(Guid.NewGuid().ToString()))).IsValid);
        Assert.False(validator.Validate(new FinalizeEvaluationCommand(0, new(Guid.NewGuid().ToString()))).IsValid);
        Assert.False(validator.Validate(new FinalizeEvaluationCommand(1, null!)).IsValid);
    }
}
