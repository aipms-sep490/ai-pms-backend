using FluentValidation;

namespace AIPMS.Application.Features.Deliverables.Queries;

public sealed class GetDeliverableQueryValidator : AbstractValidator<GetDeliverableQuery>
{ public GetDeliverableQueryValidator() => RuleFor(x => x.Id).GreaterThan(0); }
public sealed class GetDeliverablesQueryValidator : AbstractValidator<GetDeliverablesQuery>
{ public GetDeliverablesQueryValidator(){RuleFor(x=>x.ProjectId).GreaterThan(0);RuleFor(x=>x.PageNumber).GreaterThan(0);RuleFor(x=>x.PageSize).InclusiveBetween(1,100);} }
public sealed class GetDeliverableHistoryQueryValidator : AbstractValidator<GetDeliverableHistoryQuery>
{ public GetDeliverableHistoryQueryValidator() => RuleFor(x => x.DeliverableId).GreaterThan(0); }
public sealed class DownloadDeliverableFileQueryValidator : AbstractValidator<DownloadDeliverableFileQuery>
{ public DownloadDeliverableFileQueryValidator() => RuleFor(x => x.FileId).GreaterThan(0); }
