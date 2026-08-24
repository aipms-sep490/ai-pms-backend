using AIPMS.Application.Features.ProgressMeetings.Commands;

namespace AIPMS.UnitTests.Application;

public sealed class ProgressMeetingValidatorTests
{
    [Fact]
    public async Task Create_report_requires_database_period_and_summary()
    {
        var command = new CreateProgressReportCommand(1, 0, "", "", "", "", "", "");
        var result = await new CreateProgressReportValidator().ValidateAsync(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == "ReportPeriodId");
        Assert.Contains(result.Errors, x => x.PropertyName == "Summary");
    }

    [Fact]
    public async Task Create_meeting_rejects_duplicate_participants_and_bad_dates()
    {
        var command = new CreateMeetingCommand(1, "sync", null,
            new DateTime(2026, 9, 2, 10, 0, 0), new DateTime(2026, 9, 2, 9, 0, 0), null, null, [2, 2]);
        var result = await new CreateMeetingValidator().ValidateAsync(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.PropertyName == "ParticipantIds");
        Assert.Contains(result.Errors, x => x.PropertyName == "EndAt");
    }

    [Theory]
    [InlineData("INVITED")]
    [InlineData("ATTENDED")]
    [InlineData("ABSENT")]
    public async Task Attendance_accepts_schema_values(string status)
    {
        var command = new SetMeetingAttendanceCommand(1, 2, status);
        var result = await new SetMeetingAttendanceValidator().ValidateAsync(command);
        Assert.True(result.IsValid);
    }
}
