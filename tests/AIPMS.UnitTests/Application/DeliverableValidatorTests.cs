using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Models;
using AIPMS.Application.Features.Deliverables.Queries;
using AIPMS.Application.Features.Deliverables.Validators;

namespace AIPMS.UnitTests.Application;

public sealed class DeliverableValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(0, true)]
    [InlineData(42, true)]
    public void Submission_requires_explicit_bounded_expected_version(int? version, bool valid)
    {
        var command = new SubmitDeliverableVersionCommand(1, version, null, new("file.txt", "text/plain", 1, Stream.Null));
        Assert.Equal(valid, new SubmitDeliverableVersionCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Save_rejects_invalid_links_blank_title_and_ambiguous_dates()
    {
        var validator = new SaveDeliverableRequestValidator();
        var request = new SaveDeliverableRequest(null, "Title", null, null, DateTime.UnixEpoch);
        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { Title = " " }).IsValid);
        Assert.False(validator.Validate(request with { MilestoneId = 0 }).IsValid);
        Assert.False(validator.Validate(request with { DueAt = DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified) }).IsValid);
        Assert.False(validator.Validate(request with { Description = new string('a', 10001) }).IsValid);
    }

    [Theory]
    [InlineData("ACCEPTED", "Good", true)]
    [InlineData("REJECTED", "Please revise", true)]
    [InlineData("SUBMITTED", "Good", false)]
    [InlineData("ACCEPTED", " ", false)]
    public void Review_requires_supported_decision_and_feedback(string decision, string feedback, bool valid) =>
        Assert.Equal(valid, new ReviewDeliverableVersionCommandValidator().Validate(new ReviewDeliverableVersionCommand(1, decision, feedback)).IsValid);

    [Theory]
    [InlineData("VERSION", false)]
    [InlineData("FEEDBACK", false)]
    [InlineData("REPORT", true)]
    [InlineData("MEETING", true)]
    public void Standalone_upload_cannot_attach_to_immutable_records(string parentType, bool valid)
    {
        var file = new UploadContent("file.txt", "text/plain", 1, Stream.Null);
        Assert.Equal(valid, new UploadProjectFileCommandValidator().Validate(new UploadProjectFileCommand(parentType, 1, file)).IsValid);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void Paging_cannot_overflow_or_return_unbounded_results(int page, int size) =>
        Assert.False(new GetDeliverablesQueryValidator().Validate(new GetDeliverablesQuery(1, null, null, page, size)).IsValid);
}
