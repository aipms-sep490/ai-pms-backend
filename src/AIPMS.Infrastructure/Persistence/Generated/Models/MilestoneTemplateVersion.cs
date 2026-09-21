namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class MilestoneTemplateVersion
{
    public long Id { get; set; }
    public long MilestoneTemplateId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = null!;
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LockedAt { get; set; }
    public virtual MilestoneTemplate MilestoneTemplate { get; set; } = null!;
    public virtual User CreatedByNavigation { get; set; } = null!;
    public virtual ICollection<MilestoneTemplateItem> Items { get; set; } = new List<MilestoneTemplateItem>();
}
