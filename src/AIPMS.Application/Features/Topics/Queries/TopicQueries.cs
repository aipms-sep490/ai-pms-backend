using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Topics.DTOs;
using AIPMS.Application.Features.Topics.Models;
using AIPMS.Application.Features.Topics.Services;
using MediatR;

namespace AIPMS.Application.Features.Topics.Queries;

public sealed record GetTopicQuery(long Id) : IRequest<TopicDto>;
public sealed record ListTopicsQuery(TopicFilter Filter) : IRequest<PagedResult<TopicDto>>;
public sealed class TopicQueryHandler(TopicWorkflow workflow) : IRequestHandler<GetTopicQuery, TopicDto>,
    IRequestHandler<ListTopicsQuery, PagedResult<TopicDto>>
{
    public Task<TopicDto> Handle(GetTopicQuery request, CancellationToken ct) => workflow.Get(request.Id, ct);
    public Task<PagedResult<TopicDto>> Handle(ListTopicsQuery request, CancellationToken ct) => workflow.List(request.Filter, ct);
}
