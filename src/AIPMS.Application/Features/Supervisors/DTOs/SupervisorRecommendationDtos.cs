using AIPMS.Application.Common.Models;

namespace AIPMS.Application.Features.Supervisors.DTOs;

public sealed class SupervisorRecommendationSettings { public bool Enabled { get; set; } }
public sealed record SupervisorRecommendationRequest(string AssignmentType = "PRIMARY", long? MajorId = null,
    string? Search = null, int Page = 1, int PageSize = 20);
public sealed record SupervisorRecommendationItem(SupervisorCandidateDto Candidate, decimal Score,
    string ExpertiseStatus, IReadOnlyList<string> MatchedExpertise, IReadOnlyList<string> Reasons,
    bool CurrentlyEligible, IReadOnlyList<string> CurrentReasons);
public sealed record SupervisorRecommendationPreview(string EngineVersion, string AssignmentType, long? MajorId,
    DateTime GeneratedAt, PagedResult<SupervisorRecommendationItem> Candidates);
public sealed record SupervisorRecommendationRun(long Id, long ProjectId, long CreatedBy, DateTime CreatedAt,
    string EngineVersion, string AssignmentType, long? MajorId, string? Search, int Page, int PageSize,
    long TotalCount, IReadOnlyList<SupervisorRecommendationItem> Items);
public sealed record SupervisorRecommendationRunSummary(long Id, DateTime CreatedAt, long CreatedBy,
    string AssignmentType, long? MajorId, string EngineVersion, int ItemCount, long TotalCount);
