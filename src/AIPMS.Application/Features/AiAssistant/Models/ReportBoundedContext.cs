using System;
using System.Collections.Generic;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record ReportBoundedContext(
    long ProjectId,
    long ReportId,
    string ReportType,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    string Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    IReadOnlyList<EvidenceReferenceDto> EvidenceList,
    string FormattedEvidenceText,
    bool HasSufficientEvidence);
