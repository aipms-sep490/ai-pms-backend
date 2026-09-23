using System;
using System.Collections.Generic;
using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.Application.Features.AiAssistant.DTOs;

public sealed record ReportStructuredSummaryDto(
    string Completed,
    string InProgress,
    string Blockers,
    string Risks,
    string NextActions);

public sealed record ReportSummaryDto(
    long ProjectId,
    long ReportId,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    ReportStructuredSummaryDto Summary,
    string ContextScope,
    IReadOnlyList<EvidenceReferenceDto> Evidence,
    string? LimitationNote,
    DateTime GeneratedAt);
