using AIPMS.Application.Features.Deliverables.Commands;
using FluentValidation.TestHelper;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class DeliverableCommandValidatorTests
{
    [Fact]
    public void CreateDeliverable_RequiresProjectAndTitle()
    {
        var result = new CreateDeliverableCommandValidator().TestValidate(new CreateDeliverableCommand(0, null, "", null, null, null));
        result.ShouldHaveValidationErrorFor(x => x.ProjectId);
        result.ShouldHaveValidationErrorFor(x => x.Title);
    }

    [Fact]
    public void SubmitVersion_RejectsFileLargerThanLimit()
    {
        using var content = new MemoryStream([1]);
        var command = new SubmitDeliverableVersionCommand(1, null, "report.pdf", "application/pdf", 25 * 1024 * 1024 + 1, content);
        var result = new SubmitDeliverableVersionCommandValidator().TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.FileSize);
    }
}
