using AIPMS.Application.Features.Projects.Commands;
using FluentValidation;

namespace AIPMS.Application.Features.Projects.Validators;

public sealed class ArchiveProjectCommandValidator : AbstractValidator<ArchiveProjectCommand>
{
    public ArchiveProjectCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.ConcurrencyToken).NotEmpty().Must(x => Convert.TryFromBase64String(x, new byte[64], out _));
        RuleFor(x => x.Reason).MaximumLength(1000);
    }
}
