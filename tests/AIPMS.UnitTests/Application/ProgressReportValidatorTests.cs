using System;
using AIPMS.Application.Features.ProgressReports.Commands;
using AIPMS.Application.Features.ProgressReports.DTOs;
using AIPMS.Application.Features.ProgressReports.Queries;
using AIPMS.Application.Features.ProgressReports.Validators;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressReportValidatorTests
{
    private readonly CreateProgressReportValidator createValidator = new();
    private readonly UpdateProgressReportValidator updateValidator = new();
    private readonly SubmitProgressReportValidator submitValidator = new();
    private readonly AddProgressReportFeedbackValidator feedbackValidator = new();
    private readonly GetProgressReportsQueryValidator queryValidator = new();

    [Fact]
    public void CreateProgressReport_ValidCommand_PassesValidation()
    {
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", "Completed", "Planned", "Risks"));

        var result = createValidator.Validate(command);
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreateProgressReport_InvalidProjectId_FailsValidation(long projectId)
    {
        var command = new CreateProgressReportCommand(projectId, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ProjectId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("DAILY")]
    [InlineData("YEARLY")]
    public void CreateProgressReport_InvalidReportType_FailsValidation(string reportType)
    {
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            reportType, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), "Summary", null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.ReportType");
    }

    [Fact]
    public void CreateProgressReport_PeriodEndBeforePeriodStart_FailsValidation()
    {
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1), "Summary", null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.PeriodEnd");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateProgressReport_EmptySummary_FailsValidation(string summary)
    {
        var command = new CreateProgressReportCommand(1, new CreateProgressReportRequest(
            "WEEKLY", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7), summary, null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.Summary");
    }

    [Fact]
    public void UpdateProgressReport_ValidCommand_PassesValidation()
    {
        var command = new UpdateProgressReportCommand(1, new UpdateProgressReportRequest(
            "Updated summary", "Completed", "Planned", "Risks"));

        var result = updateValidator.Validate(command);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateProgressReport_EmptySummary_FailsValidation()
    {
        var command = new UpdateProgressReportCommand(1, new UpdateProgressReportRequest(
            "", null, null, null));

        var result = updateValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void SubmitProgressReport_InvalidId_FailsValidation()
    {
        var command = new SubmitProgressReportCommand(0);
        var result = submitValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddProgressReportFeedback_EmptyText_FailsValidation()
    {
        var command = new AddProgressReportFeedbackCommand(1, new AddProgressReportFeedbackRequest(""));
        var result = feedbackValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void GetProgressReportsQuery_ToDateBeforeFromDate_FailsValidation()
    {
        var query = new GetProgressReportsQuery(1, From: new DateOnly(2026, 9, 10), To: new DateOnly(2026, 9, 1));
        var result = queryValidator.Validate(query);
        Assert.False(result.IsValid);
    }
}
