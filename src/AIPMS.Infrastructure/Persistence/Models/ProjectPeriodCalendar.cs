namespace AIPMS.Infrastructure.Persistence.Models;

public sealed class ProjectPeriodCalendar
{
    public long Id { get; set; }
    public long ProjectPeriodId { get; set; }
    public int WorkingDaysOfWeek { get; set; } = 62;
    public string HolidaysJson { get; set; } = "[]";
    public Guid ConcurrencyToken { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
