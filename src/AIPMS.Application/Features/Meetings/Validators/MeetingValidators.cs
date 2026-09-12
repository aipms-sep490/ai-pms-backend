using System.Linq;
using FluentValidation;
using AIPMS.Application.Features.Meetings.Commands;
using AIPMS.Application.Features.Meetings.Queries;

namespace AIPMS.Application.Features.Meetings.Validators;

public sealed class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(255).WithMessage("Title is required and must not exceed 255 characters.");
        RuleFor(x => x.Request.StartAt).NotEmpty().WithMessage("StartAt is required.");
        When(x => x.Request.EndAt.HasValue, () =>
        {
            RuleFor(x => x.Request.EndAt!.Value)
                .GreaterThanOrEqualTo(x => x.Request.StartAt)
                .WithMessage("EndAt must be greater than or equal to StartAt.")
                .OverridePropertyName("Request.EndAt");
        });
        When(x => x.Request.ParticipantUserIds != null && x.Request.ParticipantUserIds.Count > 0, () =>
        {
            RuleFor(x => x.Request.ParticipantUserIds)
                .Must(ids => ids!.Distinct().Count() == ids!.Count)
                .WithMessage("Duplicate participant user IDs are not allowed.");
        });
    }
}

public sealed class UpdateMeetingValidator : AbstractValidator<UpdateMeetingCommand>
{
    public UpdateMeetingValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.Title).NotEmpty().MaximumLength(255).WithMessage("Title is required and must not exceed 255 characters.");
        RuleFor(x => x.Request.StartAt).NotEmpty().WithMessage("StartAt is required.");
        When(x => x.Request.EndAt.HasValue, () =>
        {
            RuleFor(x => x.Request.EndAt!.Value)
                .GreaterThanOrEqualTo(x => x.Request.StartAt)
                .WithMessage("EndAt must be greater than or equal to StartAt.")
                .OverridePropertyName("Request.EndAt");
        });
    }
}

public sealed class CancelMeetingValidator : AbstractValidator<CancelMeetingCommand>
{
    public CancelMeetingValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class CompleteMeetingValidator : AbstractValidator<CompleteMeetingCommand>
{
    public CompleteMeetingValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
    }
}

public sealed class UpdateMeetingNotesValidator : AbstractValidator<UpdateMeetingNotesCommand>
{
    public UpdateMeetingNotesValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        When(x => x.Request.Attendances != null, () =>
        {
            RuleForEach(x => x.Request.Attendances).ChildRules(a =>
            {
                a.RuleFor(i => i.UserId).GreaterThan(0);
                a.RuleFor(i => i.AttendanceStatus)
                    .Must(s => s is "INVITED" or "ACCEPTED" or "DECLINED" or "ATTENDED" or "ABSENT")
                    .WithMessage("Attendance status must be INVITED, ACCEPTED, DECLINED, ATTENDED, or ABSENT.");
            });
        });
    }
}

public sealed class AddMeetingParticipantValidator : AbstractValidator<AddMeetingParticipantCommand>
{
    public AddMeetingParticipantValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.UserId).GreaterThan(0);
        When(x => !string.IsNullOrEmpty(x.Request.AttendanceStatus), () =>
        {
            RuleFor(x => x.Request.AttendanceStatus!)
                .Must(s => s is "INVITED" or "ACCEPTED" or "DECLINED" or "ATTENDED" or "ABSENT")
                .WithMessage("Attendance status must be INVITED, ACCEPTED, DECLINED, ATTENDED, or ABSENT.");
        });
    }
}

public sealed class AddMeetingFeedbackValidator : AbstractValidator<AddMeetingFeedbackCommand>
{
    public AddMeetingFeedbackValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Request.FeedbackText).NotEmpty().WithMessage("Feedback text is required.");
    }
}

public sealed class GetMeetingsQueryValidator : AbstractValidator<GetMeetingsQuery>
{
    public GetMeetingsQueryValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        When(x => !string.IsNullOrEmpty(x.Status), () =>
        {
            RuleFor(x => x.Status!)
                .Must(s => s is "SCHEDULED" or "COMPLETED" or "CANCELLED")
                .WithMessage("Status must be SCHEDULED, COMPLETED, or CANCELLED.");
        });
        When(x => x.From.HasValue && x.To.HasValue, () =>
        {
            RuleFor(x => x.To!.Value)
                .GreaterThanOrEqualTo(x => x.From!.Value)
                .WithMessage("To date must be greater than or equal to From date.");
        });
    }
}
