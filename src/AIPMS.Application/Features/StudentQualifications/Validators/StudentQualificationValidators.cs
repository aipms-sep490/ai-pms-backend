using AIPMS.Application.Features.StudentQualifications.Commands;
using AIPMS.Application.Features.StudentQualifications.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.StudentQualifications.Validators;

public sealed class SubmitStudentQualificationEvidenceCommandValidator
    : AbstractValidator<SubmitStudentQualificationEvidenceCommand>
{
    public SubmitStudentQualificationEvidenceCommandValidator()
    {
        RuleFor(x => x.Request.QualificationType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Request.TrainingStatus).NotEmpty().MaximumLength(30);
        RuleFor(x => x.Request.CertificateNumber).MaximumLength(100);
    }
}

public sealed class GetStudentQualificationVerificationQueueQueryValidator
    : AbstractValidator<GetStudentQualificationVerificationQueueQuery>
{
    public GetStudentQualificationVerificationQueueQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.VerificationStatus).MaximumLength(30);
    }
}

public sealed class RejectStudentQualificationCommandValidator
    : AbstractValidator<RejectStudentQualificationCommand>
{
    public RejectStudentQualificationCommandValidator()
    {
        RuleFor(x => x.QualificationId).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
