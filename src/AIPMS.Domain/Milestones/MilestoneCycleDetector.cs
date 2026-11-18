using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIPMS.Domain.Milestones;

public static class MilestoneCycleDetector
{
    public static async Task<bool> HasDependencyCycleAsync(
        long milestoneId,
        long dependsOnMilestoneId,
        Func<long, Task<IEnumerable<long>>> getDependsOnMilestoneIdsAsync)
    {
        // Breadth-First Search (BFS) to find if dependsOnMilestoneId can reach milestoneId
        var visited = new HashSet<long>();
        var queue = new Queue<long>();
        queue.Enqueue(dependsOnMilestoneId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == milestoneId)
            {
                return true; // Cycle detected
            }

            if (visited.Add(current))
            {
                var dependencies = await getDependsOnMilestoneIdsAsync(current);
                foreach (var depId in dependencies)
                {
                    queue.Enqueue(depId);
                }
            }
        }

        return false;
    }
}
