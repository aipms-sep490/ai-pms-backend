using AIPMS.Infrastructure.Persistence.Models;

namespace AIPMS.Infrastructure.Persistence.Generated.Models;

public partial class Task
{
    public long? SprintId { get; set; }
    public int? StoryPoints { get; set; }
    public string TaskCode { get; set; } = "";
    public ICollection<TaskBoardLabel> BoardLabels { get; set; } = new List<TaskBoardLabel>();
}
