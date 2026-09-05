using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIPMS.Application.Features.Tasks.Domain;

public static class TaskCycleDetector
{
    public static async Task<bool> HasParentCycleAsync(
        long taskId,
        long parentTaskId,
        Func<long, Task<long?>> getParentTaskIdAsync)
    {
        var visited = new HashSet<long> { taskId };
        var currentId = (long?)parentTaskId;

        while (currentId.HasValue)
        {
            if (!visited.Add(currentId.Value))
            {
                return true; // Cycle detected
            }

            currentId = await getParentTaskIdAsync(currentId.Value);
        }

        return false;
    }

    public static async Task<bool> HasDependencyCycleAsync(
        long taskId,
        long dependsOnTaskId,
        Func<long, Task<IEnumerable<long>>> getDependsOnTaskIdsAsync)
    {
        // Breadth-First Search (BFS) to find if dependsOnTaskId can reach taskId
        var visited = new HashSet<long>();
        var queue = new Queue<long>();
        queue.Enqueue(dependsOnTaskId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == taskId)
            {
                return true; // Cycle detected
            }

            if (visited.Add(current))
            {
                var dependencies = await getDependsOnTaskIdsAsync(current);
                foreach (var depId in dependencies)
                {
                    queue.Enqueue(depId);
                }
            }
        }

        return false;
    }
}
