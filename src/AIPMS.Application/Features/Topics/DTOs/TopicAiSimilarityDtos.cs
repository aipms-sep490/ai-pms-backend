namespace AIPMS.Application.Features.Topics.DTOs;

public sealed record TopicAiSimilarityRequest(long TeamId, string Title, string? ProblemStatement,
    string? Objectives, string? ExpectedOutput, string? Domain, IReadOnlyList<string> Keywords,
    long? ProjectId = null);

public sealed record TopicAiSimilarityMatchDto(long Id, string SourceType, string Title, string SemesterCode,
    string SimilarityLevel, string Reason, IReadOnlyList<string> SharedAspects, IReadOnlyList<string> Differences,
    string CandidateExcerpt, string ProposalExcerpt);

public sealed record TopicAiSimilarityResultDto(TopicSummaryDto? ExactMatch, IReadOnlyList<SimilarTopicMatchDto> Similar,
    string GenerationStatus, IReadOnlyList<TopicAiSimilarityMatchDto> Matches, string Summary, string LimitationNote,
    int CandidateCount, int ReviewedCount, bool Truncated, DateTime GeneratedAt, string ModelVersion);
