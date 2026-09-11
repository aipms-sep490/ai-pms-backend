using System;
using System.Collections.Generic;
using AIPMS.Application.Features.Meetings.Commands;
using AIPMS.Application.Features.Meetings.DTOs;
using AIPMS.Application.Features.Meetings.Queries;
using AIPMS.Application.Features.Meetings.Validators;
using Xunit;

namespace AIPMS.UnitTests.Application;

public sealed class MeetingValidatorTests
{
    private readonly CreateMeetingValidator createValidator = new();
    private readonly UpdateMeetingValidator updateValidator = new();
    private readonly CancelMeetingValidator cancelValidator = new();
    private readonly UpdateMeetingNotesValidator notesValidator = new();
    private readonly AddMeetingParticipantValidator participantValidator = new();
    private readonly AddMeetingFeedbackValidator feedbackValidator = new();
    private readonly GetMeetingsQueryValidator queryValidator = new();

    [Fact]
    public void CreateMeeting_ValidCommand_PassesValidation()
    {
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Weekly Sync", "Discussion", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), "Room 101", null, new[] { 2L, 3L }));

        var result = createValidator.Validate(command);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void CreateMeeting_EndBeforeStart_FailsValidation()
    {
        var now = DateTime.UtcNow;
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Weekly Sync", "Discussion", now, now.AddHours(-1), null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.EndAt");
    }

    [Fact]
    public void CreateMeeting_DuplicateParticipants_FailsValidation()
    {
        var now = DateTime.UtcNow;
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            "Weekly Sync", null, now, now.AddHours(1), null, null, new[] { 2L, 2L }));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.ParticipantUserIds");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateMeeting_EmptyTitle_FailsValidation(string title)
    {
        var now = DateTime.UtcNow;
        var command = new CreateMeetingCommand(1, new CreateMeetingRequest(
            title, null, now, now.AddHours(1), null, null, null));

        var result = createValidator.Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Request.Title");
    }

    [Fact]
    public void UpdateMeeting_EndBeforeStart_FailsValidation()
    {
        var now = DateTime.UtcNow;
        var command = new UpdateMeetingCommand(1, new UpdateMeetingRequest(
            "Title", null, now, now.AddHours(-1), null, null));

        var result = updateValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void CancelMeeting_InvalidId_FailsValidation()
    {
        var command = new CancelMeetingCommand(0);
        var result = cancelValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateMeetingNotes_InvalidStatus_FailsValidation()
    {
        var command = new UpdateMeetingNotesCommand(1, new UpdateMeetingNotesRequest(
            "Notes", "INVALID_STATUS", null));

        var result = notesValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateMeetingNotes_InvalidAttendanceStatus_FailsValidation()
    {
        var command = new UpdateMeetingNotesCommand(1, new UpdateMeetingNotesRequest(
            "Notes", null, new[] { new ParticipantAttendanceUpdate(2, "MAYBE") }));

        var result = notesValidator.Validate(command);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void AddMeetingFeedback_EmptyText_FailsValidation()
    {
        var command = new AddMeetingFeedbackCommand(1, new AddMeetingFeedbackRequest(""));
        var result = feedbackValidator.Validate(command);
        Assert.False(result.IsValid);
    }
}
