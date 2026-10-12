using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal static class SprintHistory
{
    public static void Record(AipmsDbContext db, M.Task task, long sprintId, string kind, bool inScope, long actor, DateTime now) =>
        db.Set<SprintTaskEvent>().Add(new() { SprintId = sprintId, TaskId = task.Id, Kind = kind, InScope = inScope,
            TaskStatus = task.Status, StoryPoints = task.StoryPoints, ActorId = actor, OccurredAt = now });
}
