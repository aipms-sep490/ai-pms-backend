namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class RubricVersion
{
    public long RubricId { get; set; }
    public long RootRubricId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "DRAFT";
    public Guid ConcurrencyToken { get; set; }
}
