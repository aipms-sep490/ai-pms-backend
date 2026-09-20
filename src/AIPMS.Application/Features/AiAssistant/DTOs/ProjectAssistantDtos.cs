using System;
using System.Collections.Generic;
using AIPMS.Application.Features.AiAssistant.Models;

namespace AIPMS.Application.Features.AiAssistant.DTOs;

public sealed record AskProjectAssistantRequest(
    string Query);

public sealed record ProjectAssistantResponseDto(
    long ProjectId,
    string Answer,
    string ContextScope,
    IReadOnlyList<EvidenceReferenceDto> Evidence,
    string? LimitationNote,
    bool InsufficientEvidence,
    DateTime GeneratedAt);
