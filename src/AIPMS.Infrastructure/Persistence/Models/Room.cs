namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class Room
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Building { get; set; } = "";
    public int Floor { get; set; }
    public int Capacity { get; set; } = 40;
    public string Status { get; set; } = "ACTIVE";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
