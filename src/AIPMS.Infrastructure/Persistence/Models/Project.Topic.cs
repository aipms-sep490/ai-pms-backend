using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Project
{
    public long? TopicId { get; set; }
    public string ProposalSource { get; set; } = "STUDENT_PROPOSAL";
    public virtual ProjectTopic? Topic { get; set; }
}
