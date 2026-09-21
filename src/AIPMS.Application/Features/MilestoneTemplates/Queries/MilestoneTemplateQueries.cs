using AIPMS.Application.Features.MilestoneTemplates.Abstractions;
using AIPMS.Application.Features.MilestoneTemplates.DTOs;
using MediatR;
namespace AIPMS.Application.Features.MilestoneTemplates.Queries;
public sealed record GetMilestoneTemplatesQuery : IRequest<IReadOnlyList<MilestoneTemplateDto>>;
public sealed class GetMilestoneTemplatesHandler(IMilestoneTemplateRepository repository) : IRequestHandler<GetMilestoneTemplatesQuery, IReadOnlyList<MilestoneTemplateDto>>
{ public Task<IReadOnlyList<MilestoneTemplateDto>> Handle(GetMilestoneTemplatesQuery r, CancellationToken ct) => repository.ListAsync(ct); }
