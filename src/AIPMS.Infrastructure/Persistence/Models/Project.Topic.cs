using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Project
{
    public long? TopicId { get; set; }
    public string ProposalSource { get; set; } = "STUDENT_PROPOSAL";
    public string? TrackCode { get; set; }
    public string? IntegrationDeliverableSummary { get; set; }
    public string? MktAttributionLimits { get; set; }
    public virtual ProjectTopic? Topic { get; set; }
}
