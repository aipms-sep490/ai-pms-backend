namespace AIPMS.Infrastructure.Persistence.Generated.Models;
public partial class TaskComment
{
    public long Id { get; set; }
    public long TaskId { get; set; }
    public long AuthorId { get; set; }
    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual Task Task { get; set; } = null!;
    public virtual User Author { get; set; } = null!;
}
