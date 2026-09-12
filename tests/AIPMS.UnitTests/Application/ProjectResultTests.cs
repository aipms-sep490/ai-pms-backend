using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Results.Commands;
using AIPMS.Application.Features.Results.DTOs;
using AIPMS.Application.Features.Results.Services;
using AIPMS.Application.Features.Results.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class ProjectResultTests
{
    [Fact]
    public void Weighted_scores_round_once_with_midpoints_away_from_zero()
    {
        ResultContributionDto[] inputs = [new(1, 10, 100, 1000, 50, 5, "a"), new(2, 20, 200, 1000, 50, 5.01m, "b")];
        Assert.Equal(5.01m, ResultScoring.Total(inputs, 5.01m));
        Assert.Equal(5.01m, ResultScoring.Total(inputs.Reverse().ToArray(), 5.01m));
        Assert.Throws<ConflictException>(() => ResultScoring.Total([inputs[0], inputs[0]], 5));
        Assert.Throws<ConflictException>(() => ResultScoring.Total([inputs[0]], 5));
        Assert.Throws<ConflictException>(() => ResultScoring.Total([inputs[0] with { Score = 11 }, inputs[1]], 5));
    }

    public static IEnumerable<object[]> InvalidPolicies()
    {
        var valid = new ConfigureResultPolicyRequest(5, [new(1, 100)], null);
        yield return [valid with { PassThreshold = -1 }];
        yield return [valid with { PassThreshold = 10.01m }];
        yield return [valid with { PassThreshold = 5.001m }];
        yield return [valid with { ConcurrencyToken = "bad" }];
        yield return [valid with { Assignments = null! }];
        yield return [valid with { Assignments = [] }];
        yield return [valid with { Assignments = [null!] }];
        yield return [valid with { Assignments = [new(0, 100)] }];
        yield return [valid with { Assignments = [new(1, 50), new(1, 50)] }];
        yield return [valid with { Assignments = [new(1, 99)] }];
        yield return [valid with { Assignments = [new(1, 100), new(2, 0)] }];
        yield return [valid with { Assignments = [new(1, 50.001m), new(2, 49.999m)] }];
        yield return [valid with { Assignments = [new(1, decimal.MaxValue), new(2, decimal.MaxValue)] }];
    }
    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public void Invalid_policy_returns_validation_failure_without_throwing(ConfigureResultPolicyRequest policy) =>
        Assert.False(new ConfigureResultPolicyRequestValidator().Validate(policy).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    public void Publication_needs_preview_confirmation(string? token) => Assert.False(
        new PublishProjectResultCommandValidator().Validate(new PublishProjectResultCommand(1, new(token!))).IsValid);

    [Fact]
    public void Commands_require_positive_project_and_nonnull_payload()
    {
        var configure = new ConfigureResultPolicyCommandValidator();
        Assert.True(configure.Validate(new ConfigureResultPolicyCommand(1, new(0, [new(1, 100)], null))).IsValid);
        Assert.False(configure.Validate(new ConfigureResultPolicyCommand(0, new(10, [new(1, 100)], null))).IsValid);
        Assert.False(configure.Validate(new ConfigureResultPolicyCommand(1, null!)).IsValid);
        var publish = new PublishProjectResultCommandValidator();
        Assert.True(publish.Validate(new PublishProjectResultCommand(1, new(new string('a', 64)))).IsValid);
        Assert.False(publish.Validate(new PublishProjectResultCommand(1, new(new string('a', 64) + "\n"))).IsValid);
        Assert.False(publish.Validate(new PublishProjectResultCommand(1, null!)).IsValid);
    }
}
