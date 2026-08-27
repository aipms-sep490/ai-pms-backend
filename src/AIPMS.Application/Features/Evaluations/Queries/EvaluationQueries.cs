using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.Commands;
using AIPMS.Application.Features.Evaluations.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Queries;

public sealed record GetEvaluationQuery(long Id) : IRequest<EvaluationDetailDto>;
public sealed record GetProjectEvaluationsQuery(long ProjectId, int PageNumber = 1,
    int PageSize = 20) : IRequest<PagedResult<EvaluationDto>>;
public sealed class GetEvaluationValidator : AbstractValidator<GetEvaluationQuery>
{ public GetEvaluationValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class GetProjectEvaluationsValidator : AbstractValidator<GetProjectEvaluationsQuery>
{ public GetProjectEvaluationsValidator() { RuleFor(x => x.ProjectId).GreaterThan(0); RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }

public sealed class GetEvaluationHandler(ICurrentUser user, IEvaluationRepository repository)
    : IRequestHandler<GetEvaluationQuery, EvaluationDetailDto>
{
    public async Task<EvaluationDetailDto> Handle(GetEvaluationQuery request, CancellationToken ct)
    {
        var item = await repository.GetAsync(request.Id, ct) ?? throw new NotFoundException("Evaluation", request.Id);
        await EvaluationAccess.Ensure(EvaluationAccess.UserId(user), item, repository, ct);
        return item;
    }
}
public sealed class GetProjectEvaluationsHandler(ICurrentUser user, IEvaluationRepository repository)
    : IRequestHandler<GetProjectEvaluationsQuery, PagedResult<EvaluationDto>>
{
    public async Task<PagedResult<EvaluationDto>> Handle(GetProjectEvaluationsQuery request, CancellationToken ct)
    {
        if (!await repository.CanEvaluateProjectAsync(EvaluationAccess.UserId(user), request.ProjectId, ct))
            throw new ForbiddenException();
        return await repository.GetByProjectAsync(request.ProjectId, request.PageNumber, request.PageSize, ct);
    }
}

