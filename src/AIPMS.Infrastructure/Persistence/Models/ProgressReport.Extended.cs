using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class ProgressReport
{
    public long? ProgressReportPeriodId { get; set; }
    public bool? IsLate { get; set; }
    public string? InProgressWork { get; set; }
    public string? Blockers { get; set; }
    public string? Risks { get; set; }
    public string? NextActions { get; set; }
    public virtual ProgressReportPeriod? ProgressReportPeriod { get; set; }
}
