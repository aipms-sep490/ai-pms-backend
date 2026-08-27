using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Evaluations.Abstractions;
using AIPMS.Application.Features.Evaluations.DTOs;
using FluentValidation;
using MediatR;

namespace AIPMS.Application.Features.Evaluations.Queries;
public sealed record GetRubricQuery(long Id) : IRequest<RubricDto>;
public sealed record ListRubricsQuery(bool? Active, int PageNumber = 1, int PageSize = 20) : IRequest<PagedResult<RubricDto>>;
public sealed class GetRubricValidator : AbstractValidator<GetRubricQuery> { public GetRubricValidator() { RuleFor(x => x.Id).GreaterThan(0); } }
public sealed class ListRubricsValidator : AbstractValidator<ListRubricsQuery> { public ListRubricsValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); } }
public sealed class GetRubricHandler(IEvaluationRepository repository) : IRequestHandler<GetRubricQuery, RubricDto> { public async Task<RubricDto> Handle(GetRubricQuery r, CancellationToken ct) => await repository.GetRubricAsync(r.Id, ct) ?? throw new NotFoundException("Rubric", r.Id); }
public sealed class ListRubricsHandler(IEvaluationRepository repository) : IRequestHandler<ListRubricsQuery, PagedResult<RubricDto>> { public Task<PagedResult<RubricDto>> Handle(ListRubricsQuery r, CancellationToken ct) => repository.ListRubricsAsync(r.Active, r.PageNumber, r.PageSize, ct); }

