using System.Collections.Generic;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record ReportEvidencePayload(
    long ReportId,
    long ProjectId,
    string ReportType,
    string Period,
    string Status,
    string? Summary,
    string? CompletedWork,
    string? PlannedWork,
    string? IssuesAndRisks,
    IReadOnlyList<ReportFeedbackEvidenceItem> Feedbacks,
    string? InProgressWork = null,
    string? Blockers = null,
    string? Risks = null,
    string? NextActions = null);

public sealed record ReportFeedbackEvidenceItem(
    string SupervisorName,
    string CreatedAt,
    string FeedbackText);
