namespace AIPMS.Application.Features.MilestoneTemplates.DTOs;

public sealed record SaveMilestoneTemplateRequest(string Name, string? Description);
public sealed record SaveMilestoneTemplateItemRequest(string Title, string? Description, int? StartOffsetDays, int? DueOffsetDays, int SortOrder);
public sealed record MilestoneTemplateItemDto(long Id, string Title, string? Description, int? StartOffsetDays, int? DueOffsetDays, int SortOrder);
public sealed record MilestoneTemplateVersionDto(long Id, int VersionNumber, string Status, IReadOnlyList<MilestoneTemplateItemDto> Items);
public sealed record MilestoneTemplateDto(long Id, string Name, string? Description, string Status, IReadOnlyList<MilestoneTemplateVersionDto> Versions);
