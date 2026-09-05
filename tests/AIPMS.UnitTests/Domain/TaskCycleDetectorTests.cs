using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIPMS.Application.Features.Tasks.Domain;
using Xunit;

namespace AIPMS.UnitTests.Domain;

public sealed class TaskCycleDetectorTests
{
    [Fact]
    public async Task HasParentCycleAsync_DirectCycle_ShouldReturnTrue()
    {
        var result = await TaskCycleDetector.HasParentCycleAsync(1, 1, _ => Task.FromResult<long?>(null));
        Assert.True(result);
    }

    [Fact]
    public async Task HasParentCycleAsync_MultiLevelCycle_ShouldReturnTrue()
    {
        var parentChain = new Dictionary<long, long?>
        {
            [2] = 3,
            [3] = 1 // 1 is parentTaskId, taskId is 1. Chain: 1 -> 2 -> 3 -> 1
        };

        var result = await TaskCycleDetector.HasParentCycleAsync(
            taskId: 1,
            parentTaskId: 2,
            getParentTaskIdAsync: id => Task.FromResult(parentChain.TryGetValue(id, out var p) ? p : null));

        Assert.True(result);
    }

    [Fact]
    public async Task HasParentCycleAsync_NoCycle_ShouldReturnFalse()
    {
        var parentChain = new Dictionary<long, long?>
        {
            [2] = 3,
            [3] = null
        };

        var result = await TaskCycleDetector.HasParentCycleAsync(
            taskId: 1,
            parentTaskId: 2,
            getParentTaskIdAsync: id => Task.FromResult(parentChain.TryGetValue(id, out var p) ? p : null));

        Assert.False(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_DirectCycle_ShouldReturnTrue()
    {
        var deps = new Dictionary<long, IEnumerable<long>>
        {
            [2] = [1] // 2 depends on 1
        };

        // We want to add dependency: 1 depends on 2. So taskId = 1, dependsOnTaskId = 2.
        var result = await TaskCycleDetector.HasDependencyCycleAsync(
            taskId: 1,
            dependsOnTaskId: 2,
            getDependsOnTaskIdsAsync: id => Task.FromResult(deps.TryGetValue(id, out var d) ? d : []));

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_MultiLevelCycle_ShouldReturnTrue()
    {
        var deps = new Dictionary<long, IEnumerable<long>>
        {
            [2] = [3], // 2 depends on 3
            [3] = [1]  // 3 depends on 1
        };

        // We want to add dependency: 1 depends on 2. So taskId = 1, dependsOnTaskId = 2.
        var result = await TaskCycleDetector.HasDependencyCycleAsync(
            taskId: 1,
            dependsOnTaskId: 2,
            getDependsOnTaskIdsAsync: id => Task.FromResult(deps.TryGetValue(id, out var d) ? d : []));

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_NoCycle_ShouldReturnFalse()
    {
        var deps = new Dictionary<long, IEnumerable<long>>
        {
            [2] = [3], // 2 depends on 3
            [3] = []
        };

        // We want to add dependency: 1 depends on 2. So taskId = 1, dependsOnTaskId = 2.
        var result = await TaskCycleDetector.HasDependencyCycleAsync(
            taskId: 1,
            dependsOnTaskId: 2,
            getDependsOnTaskIdsAsync: id => Task.FromResult(deps.TryGetValue(id, out var d) ? d : []));

        Assert.False(result);
    }
}
