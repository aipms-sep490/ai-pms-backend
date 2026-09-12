using AIPMS.Application.Features.Projects.Commands;
using AIPMS.Application.Features.Projects.DTOs;
using FluentValidation;

namespace AIPMS.Application.Features.Projects.Validators;

public sealed class RecordDepartmentDecisionCommandValidator : AbstractValidator<RecordDepartmentDecisionCommand>
{
    public RecordDepartmentDecisionCommandValidator()
    {
        RuleFor(x => x.ProjectId).GreaterThan(0);
        RuleFor(x => x.Decision).NotNull();
        When(x => x.Decision is not null, () =>
        {
            RuleFor(x => x.Decision.SnapshotId).GreaterThan(0);
            RuleFor(x => x.Decision.ConcurrencyToken).NotEmpty().MaximumLength(100);
            RuleFor(x => x.Decision.Decision).Must(x => x is "APPROVED" or "REJECTED");
            RuleFor(x => x.Decision.Reason).MaximumLength(2000);
            RuleFor(x => x.Decision.Reason).NotEmpty().When(x => x.Decision.Decision == "REJECTED");
        });
    }
}
