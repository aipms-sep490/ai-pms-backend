namespace AIPMS.Domain.Entities;

public sealed class SupervisorProfile
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string? Bio { get; set; }
    public int? MaxActiveProjects { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
