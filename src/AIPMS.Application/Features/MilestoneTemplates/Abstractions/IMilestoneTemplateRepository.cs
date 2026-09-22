using AIPMS.Application.Features.MilestoneTemplates.DTOs;

namespace AIPMS.Application.Features.MilestoneTemplates.Abstractions;

public interface IMilestoneTemplateRepository
{
    Task<IReadOnlyList<MilestoneTemplateDto>> ListAsync(CancellationToken ct);
    Task<MilestoneTemplateDto> CreateAsync(string name, string? description, long actorId, DateTime now, CancellationToken ct);
    Task<MilestoneTemplateVersionDto> CreateVersionAsync(long templateId, long actorId, DateTime now, CancellationToken ct);
    Task UpdateAsync(long templateId, string name, string? description, long actorId, DateTime now, CancellationToken ct);
    Task DeleteAsync(long templateId, long actorId, CancellationToken ct);
    Task PublishVersionAsync(long versionId, long actorId, DateTime now, CancellationToken ct);
    Task<MilestoneTemplateVersionDto> AddItemAsync(long versionId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct);
    Task<MilestoneTemplateVersionDto> UpdateItemAsync(long itemId, SaveMilestoneTemplateItemRequest request, long actorId, DateTime now, CancellationToken ct);
    Task DeleteItemAsync(long itemId, long actorId, CancellationToken ct);
    Task AssignAsync(long periodId, long templateId, long? versionId, long actorId, DateTime now, CancellationToken ct);
}
