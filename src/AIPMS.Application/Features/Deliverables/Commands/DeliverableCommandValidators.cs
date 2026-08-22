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
