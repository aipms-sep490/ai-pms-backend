using AIPMS.Application.Features.Topics.DTOs;

namespace AIPMS.Application.Features.Topics.Abstractions;

public interface ITopicSimilarityReviewService
{
    Task<TopicAiSimilarityResultDto> CheckAsync(TopicAiSimilarityRequest input, CancellationToken ct);
}
