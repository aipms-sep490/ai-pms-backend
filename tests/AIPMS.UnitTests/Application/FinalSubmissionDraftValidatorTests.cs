using AIPMS.Application.Features.FinalSubmissions.Commands;
using AIPMS.Application.Features.FinalSubmissions.DTOs;
using AIPMS.Application.Features.FinalSubmissions.Queries;
using AIPMS.Application.Features.FinalSubmissions.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class FinalSubmissionDraftValidatorTests
{
    [Fact]
    public void Empty_draft_is_valid_but_missing_selection_collection_is_not()
    {
        var validator = new CreateFinalSubmissionDraftRequestValidator();
        Assert.True(validator.Validate(new CreateFinalSubmissionDraftRequest(1, null, [])).IsValid);
        Assert.False(validator.Validate(new CreateFinalSubmissionDraftRequest(1, null, null!)).IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Version_identifiers_must_be_positive(long id) =>
        Assert.False(new CreateFinalSubmissionDraftRequestValidator().Validate(new CreateFinalSubmissionDraftRequest(1, null, [id])).IsValid);

    [Fact]
    public void Repeated_versions_and_oversized_selection_are_rejected()
    {
        var validator = new CreateFinalSubmissionDraftRequestValidator();
        Assert.False(validator.Validate(new CreateFinalSubmissionDraftRequest(1, null, [1, 1])).IsValid);
        Assert.True(validator.Validate(new CreateFinalSubmissionDraftRequest(1, null, Enumerable.Range(1, 100).Select(x => (long)x).ToArray())).IsValid);
        Assert.False(validator.Validate(new CreateFinalSubmissionDraftRequest(1, null, Enumerable.Range(1, 101).Select(x => (long)x).ToArray())).IsValid);
    }

    [Fact]
    public void Notes_length_matches_persistence_limit()
    {
        var validator = new UpdateFinalSubmissionDraftRequestValidator();
        var input = new UpdateFinalSubmissionDraftRequest(1, new string('x', 10000), [], Guid.NewGuid().ToString());
        Assert.True(validator.Validate(input).IsValid);
        Assert.False(validator.Validate(input with { Notes = new string('x', 10001) }).IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stale-text")]
    public void Updates_require_a_valid_concurrency_token(string? token) =>
        Assert.False(new UpdateFinalSubmissionDraftRequestValidator().Validate(new UpdateFinalSubmissionDraftRequest(1, null, [], token!)).IsValid);

    [Fact]
    public void Commands_reject_null_input_and_invalid_project_ids()
    {
        Assert.False(new CreateFinalSubmissionDraftCommandValidator().Validate(new CreateFinalSubmissionDraftCommand(1, null!)).IsValid);
        Assert.False(new UpdateFinalSubmissionDraftCommandValidator().Validate(new UpdateFinalSubmissionDraftCommand(1, null!)).IsValid);
        Assert.False(new CreateFinalSubmissionDraftCommandValidator().Validate(new CreateFinalSubmissionDraftCommand(0, new(1, null, []))).IsValid);
        Assert.False(new GetFinalSubmissionDraftQueryValidator().Validate(new GetFinalSubmissionDraftQuery(-1)).IsValid);
    }

    [Fact]
    public void Window_pagination_cannot_overflow_or_return_unbounded_results()
    {
        var validator = new GetFinalSubmissionPeriodsQueryValidator();
        Assert.True(validator.Validate(new GetFinalSubmissionPeriodsQuery(1, 1000000, 100)).IsValid);
        Assert.False(validator.Validate(new GetFinalSubmissionPeriodsQuery(1, int.MaxValue, 100)).IsValid);
        Assert.False(validator.Validate(new GetFinalSubmissionPeriodsQuery(1, 0, 20)).IsValid);
        Assert.False(validator.Validate(new GetFinalSubmissionPeriodsQuery(1, 1, 101)).IsValid);
    }
}
