using AIPMS.Application.Features.Topics.Abstractions;
using AIPMS.Application.Features.Topics.DTOs;
using MediatR;

namespace AIPMS.Application.Features.Topics.Queries;

public sealed record CheckTopicAiSimilarityQuery(TopicAiSimilarityRequest Request) : IRequest<TopicAiSimilarityResultDto>;

public sealed class CheckTopicAiSimilarityQueryHandler(ITopicSimilarityReviewService service)
    : IRequestHandler<CheckTopicAiSimilarityQuery, TopicAiSimilarityResultDto>
{
    public Task<TopicAiSimilarityResultDto> Handle(CheckTopicAiSimilarityQuery request, CancellationToken ct) =>
        service.CheckAsync(request.Request, ct);
}
