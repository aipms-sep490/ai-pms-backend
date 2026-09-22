namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class MilestoneTemplate
{
    public long Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public long CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual User CreatedByNavigation { get; set; } = null!;
    public virtual ICollection<MilestoneTemplateVersion> Versions { get; set; } = new List<MilestoneTemplateVersion>();
}
