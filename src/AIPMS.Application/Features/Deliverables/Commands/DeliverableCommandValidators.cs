using FluentValidation;

namespace AIPMS.Application.Features.Deliverables.Commands;

public sealed class CreateDeliverableCommandValidator : AbstractValidator<CreateDeliverableCommand>
{
    public CreateDeliverableCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
        RuleFor(x => x.MilestoneId).GreaterThan(0).When(x => x.MilestoneId.HasValue);
    }
}

public sealed class UpdateDeliverableCommandValidator : AbstractValidator<UpdateDeliverableCommand>
{
    public UpdateDeliverableCommandValidator()
    {
        RuleFor(x => x.Id).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(255);
    }
}

public sealed class SubmitDeliverableVersionCommandValidator : AbstractValidator<SubmitDeliverableVersionCommand>
{
    public SubmitDeliverableVersionCommandValidator()
    {
        RuleFor(x => x.DeliverableId).GreaterThan(0);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FileSize).GreaterThan(0).LessThanOrEqualTo(25 * 1024 * 1024);
        RuleFor(x => x.Content).NotNull();
    }
}

public sealed class UploadDeliverableFileCommandValidator : AbstractValidator<UploadDeliverableFileCommand>
{
    public UploadDeliverableFileCommandValidator()
    {
        RuleFor(x => x.DeliverableId).GreaterThan(0);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(255);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(255);
        RuleFor(x => x.FileSize).GreaterThan(0).LessThanOrEqualTo(25 * 1024 * 1024);
        RuleFor(x => x.Content).NotNull();
        RuleFor(x => x.Note).MaximumLength(4000);
    }
}

public sealed class DeleteDeliverableCommandValidator : AbstractValidator<DeleteDeliverableCommand>
{ public DeleteDeliverableCommandValidator() => RuleFor(x => x.Id).GreaterThan(0); }
public sealed class DeleteDeliverableFileCommandValidator : AbstractValidator<DeleteDeliverableFileCommand>
{ public DeleteDeliverableFileCommandValidator() => RuleFor(x => x.FileId).GreaterThan(0); }
public sealed class AddSupervisorFeedbackCommandValidator : AbstractValidator<AddSupervisorFeedbackCommand>
{ public AddSupervisorFeedbackCommandValidator(){RuleFor(x=>x.DeliverableVersionId).GreaterThan(0);RuleFor(x=>x.FeedbackText).NotEmpty().MaximumLength(4000);} }
public sealed class RequestDeliverableRevisionCommandValidator : AbstractValidator<RequestDeliverableRevisionCommand>
{ public RequestDeliverableRevisionCommandValidator(){RuleFor(x=>x.DeliverableId).GreaterThan(0);RuleFor(x=>x.Reason).NotEmpty().MaximumLength(4000);} }
