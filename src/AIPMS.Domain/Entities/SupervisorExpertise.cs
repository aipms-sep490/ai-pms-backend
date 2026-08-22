namespace AIPMS.Domain.Entities;

public sealed class SupervisorExpertise
{
    public long Id { get; set; }
    public long SupervisorProfileId { get; set; }
    public string ExpertiseName { get; set; } = null!;
    public string? ProficiencyLevel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
