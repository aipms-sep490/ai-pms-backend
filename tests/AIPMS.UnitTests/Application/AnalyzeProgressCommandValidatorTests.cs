using AIPMS.Application.Features.ProgressReports.Commands.AnalyzeProgress;

namespace AIPMS.UnitTests.Application;

public sealed class AnalyzeProgressCommandValidatorTests
{
    [Fact]
    public async Task Validate_OverdueTasksExceedTotal_ReturnsValidationError()
    {
        var validator = new AnalyzeProgressCommandValidator();
        var command = new AnalyzeProgressCommand(2, 3, 0, 0.5m);

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(AnalyzeProgressCommand.OverdueTasks));
    }

    [Fact]
    public async Task Validate_ValidInput_ReturnsNoValidationError()
    {
        var validator = new AnalyzeProgressCommandValidator();
        var command = new AnalyzeProgressCommand(10, 1, 2, 0.7m);

        var result = await validator.ValidateAsync(command);

        Assert.True(result.IsValid);
    }
}
