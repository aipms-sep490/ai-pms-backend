using System.Collections.Generic;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record ProjectBoundedContext(
    long ProjectId,
    string ProjectStatus,
    IReadOnlyList<EvidenceReferenceDto> EvidenceList,
    string FormattedEvidenceText,
    bool HasSufficientEvidence,
    int TotalEvidenceCount);
