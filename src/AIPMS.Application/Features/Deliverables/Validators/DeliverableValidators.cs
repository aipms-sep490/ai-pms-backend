using AIPMS.Application.Features.Deliverables.Commands;
using AIPMS.Application.Features.Deliverables.DTOs;
using AIPMS.Application.Features.Deliverables.Queries;
using FluentValidation;

namespace AIPMS.Application.Features.Deliverables.Validators;

public sealed class SaveDeliverableRequestValidator : AbstractValidator<SaveDeliverableRequest>
{
    public SaveDeliverableRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(255);
        RuleFor(r => r.Description).MaximumLength(10000);
        RuleFor(r => r.DeliverableType).MaximumLength(50);
        RuleFor(r => r.MilestoneId).GreaterThan(0).When(r => r.MilestoneId.HasValue);
        RuleFor(r => r.DueAt).Must(d => d is null || d.Value.Kind == DateTimeKind.Utc).WithMessage("DueAt must be UTC (Z).");
    }
}
public sealed class CreateDeliverableCommandValidator : AbstractValidator<CreateDeliverableCommand>
{
    public CreateDeliverableCommandValidator() { RuleFor(r => r.ProjectId).GreaterThan(0); RuleFor(r => r.Data).NotNull().SetValidator(new SaveDeliverableRequestValidator()); }
}
public sealed class UpdateDeliverableCommandValidator : AbstractValidator<UpdateDeliverableCommand>
{
    public UpdateDeliverableCommandValidator() { RuleFor(r => r.Id).GreaterThan(0); RuleFor(r => r.Data).NotNull().SetValidator(new SaveDeliverableRequestValidator()); }
}
public sealed class DeleteDeliverableCommandValidator : AbstractValidator<DeleteDeliverableCommand>
{
    public DeleteDeliverableCommandValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class SubmitDeliverableVersionCommandValidator : AbstractValidator<SubmitDeliverableVersionCommand>
{
    public SubmitDeliverableVersionCommandValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.ExpectedLatestVersion).NotNull().InclusiveBetween(0, int.MaxValue - 1);
        RuleFor(r => r.Note).MaximumLength(2000);
        RuleFor(r => r.File).NotNull();
    }
}
public sealed class ReviewDeliverableVersionCommandValidator : AbstractValidator<ReviewDeliverableVersionCommand>
{
    public ReviewDeliverableVersionCommandValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Decision).Must(s => s is "ACCEPTED" or "REJECTED");
        RuleFor(r => r.Feedback).NotEmpty().MaximumLength(10000);
    }
}
public sealed class UploadProjectFileCommandValidator : AbstractValidator<UploadProjectFileCommand>
{
    public UploadProjectFileCommandValidator()
    {
        RuleFor(r => r.ParentId).GreaterThan(0);
        RuleFor(r => r.ParentType).Must(s => s is "REPORT" or "MEETING");
        RuleFor(r => r.File).NotNull();
    }
}
public sealed class DeleteProjectFileCommandValidator : AbstractValidator<DeleteProjectFileCommand>
{
    public DeleteProjectFileCommandValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class GetDeliverableQueryValidator : AbstractValidator<GetDeliverableQuery>
{
    public GetDeliverableQueryValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class GetDeliverableVersionQueryValidator : AbstractValidator<GetDeliverableVersionQuery>
{
    public GetDeliverableVersionQueryValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class GetProjectFileQueryValidator : AbstractValidator<GetProjectFileQuery>
{
    public GetProjectFileQueryValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class DownloadProjectFileQueryValidator : AbstractValidator<DownloadProjectFileQuery>
{
    public DownloadProjectFileQueryValidator() => RuleFor(r => r.Id).GreaterThan(0);
}
public sealed class GetDeliverablesQueryValidator : AbstractValidator<GetDeliverablesQuery>
{
    public GetDeliverablesQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Status).Must(s => s is null or "DRAFT" or "OPEN" or "SUBMITTED" or "ACCEPTED" or "REJECTED" or "CLOSED");
        RuleFor(r => r.MilestoneId).GreaterThan(0).When(r => r.MilestoneId.HasValue);
        RuleFor(r => r.DeliverableType).MaximumLength(50);
        RuleFor(r => r.Search).MaximumLength(255);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetDeliverableVersionsQueryValidator : AbstractValidator<GetDeliverableVersionsQuery>
{
    public GetDeliverableVersionsQueryValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetDeliverableFeedbackQueryValidator : AbstractValidator<GetDeliverableFeedbackQuery>
{
    public GetDeliverableFeedbackQueryValidator()
    {
        RuleFor(r => r.Id).GreaterThan(0);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
public sealed class GetProjectFilesQueryValidator : AbstractValidator<GetProjectFilesQuery>
{
    public GetProjectFilesQueryValidator()
    {
        RuleFor(r => r.ProjectId).GreaterThan(0);
        RuleFor(r => r.Search).MaximumLength(255);
        RuleFor(r => r.ContentType).MaximumLength(100);
        RuleFor(r => r.UploadedBy).GreaterThan(0).When(r => r.UploadedBy.HasValue);
        RuleFor(r => r.ParentType).Must(s => s is null or "VERSION" or "REPORT" or "MEETING" or "FEEDBACK");
        RuleFor(r => r.ParentId).GreaterThan(0).When(r => r.ParentId.HasValue);
        RuleFor(r => r.ParentType).NotEmpty().When(r => r.ParentId.HasValue);
        RuleFor(r => r.From).Must(d => d is null || d.Value.Kind == DateTimeKind.Utc).WithMessage("From must be UTC (Z).");
        RuleFor(r => r.To).Must(d => d is null || d.Value.Kind == DateTimeKind.Utc).WithMessage("To must be UTC (Z).");
        RuleFor(r => r.To).GreaterThan(r => r.From).When(r => r.From.HasValue && r.To.HasValue);
        RuleFor(r => r.Page).InclusiveBetween(1, 1_000_000);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
