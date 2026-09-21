namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class ProjectMilestoneTemplateApplication
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public long MilestoneTemplateVersionId { get; set; }
    public long AppliedBy { get; set; }
    public DateTime AppliedAt { get; set; }
    public virtual Project Project { get; set; } = null!;
    public virtual MilestoneTemplateVersion MilestoneTemplateVersion { get; set; } = null!;
    public virtual User AppliedByNavigation { get; set; } = null!;
}
