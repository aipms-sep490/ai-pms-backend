using System;

namespace AIPMS.Application.Features.AiAssistant.Models;

public sealed record EvidenceReferenceDto(
    string SourceType,
    string SourceId,
    string Title,
    string? PeriodOrDate = null,
    string? ReferenceUrl = null,
    string? Excerpt = null);
