using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIPMS.Domain.Milestones;
using Xunit;

namespace AIPMS.UnitTests.Domain;

public sealed class MilestoneCycleDetectorTests
{
    [Fact]
    public async Task HasDependencyCycleAsync_SelfDependency_ShouldReturnTrue()
    {
        var result = await MilestoneCycleDetector.HasDependencyCycleAsync(
            milestoneId: 10,
            dependsOnMilestoneId: 10,
            getDependsOnMilestoneIdsAsync: _ => Task.FromResult<IEnumerable<long>>(Array.Empty<long>()));

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_DirectCycle_ShouldReturnTrue()
    {
        // 1 depends on 2. Now adding 2 depends on 1.
        var graph = new Dictionary<long, List<long>>
        {
            [1] = new() { 2 }
        };

        var result = await MilestoneCycleDetector.HasDependencyCycleAsync(
            milestoneId: 2,
            dependsOnMilestoneId: 1,
            getDependsOnMilestoneIdsAsync: id => Task.FromResult<IEnumerable<long>>(graph.TryGetValue(id, out var list) ? list : Array.Empty<long>()));

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_MultiHopCycle_ShouldReturnTrue()
    {
        // 1 -> 2 -> 3. Now adding 3 -> 1.
        var graph = new Dictionary<long, List<long>>
        {
            [1] = new() { 2 },
            [2] = new() { 3 }
        };

        var result = await MilestoneCycleDetector.HasDependencyCycleAsync(
            milestoneId: 3,
            dependsOnMilestoneId: 1,
            getDependsOnMilestoneIdsAsync: id => Task.FromResult<IEnumerable<long>>(graph.TryGetValue(id, out var list) ? list : Array.Empty<long>()));

        Assert.True(result);
    }

    [Fact]
    public async Task HasDependencyCycleAsync_AcyclicGraph_ShouldReturnFalse()
    {
        // 1 -> 2, 3 -> 2. Now adding 4 -> 3.
        var graph = new Dictionary<long, List<long>>
        {
            [1] = new() { 2 },
            [3] = new() { 2 }
        };

        var result = await MilestoneCycleDetector.HasDependencyCycleAsync(
            milestoneId: 4,
            dependsOnMilestoneId: 3,
            getDependsOnMilestoneIdsAsync: id => Task.FromResult<IEnumerable<long>>(graph.TryGetValue(id, out var list) ? list : Array.Empty<long>()));

        Assert.False(result);
    }
}
