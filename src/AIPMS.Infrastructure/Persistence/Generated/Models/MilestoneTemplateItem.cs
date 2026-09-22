namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class MilestoneTemplateItem
{
    public long Id { get; set; }
    public long MilestoneTemplateVersionId { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public int? StartOffsetDays { get; set; }
    public int? DueOffsetDays { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual MilestoneTemplateVersion MilestoneTemplateVersion { get; set; } = null!;
}
