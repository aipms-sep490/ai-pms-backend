using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.Application.Features.AiAssistant.DTOs;

public sealed record AiRunDto(long Id, long ProjectId, string Kind, string Status, string ModelVersion,
    string ConfigVersion, DateTime StartedAt, DateTime? CompletedAt, string? ErrorCode,
    IReadOnlyList<EvidenceReferenceDto> Evidence, ProjectAssistantResponseDto? Answer, ReportSummaryDto? ReportSummary);
