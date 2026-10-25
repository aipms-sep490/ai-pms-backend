using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIPMS.Api.Services;
using AIPMS.Application.Features.Projects.Abstractions;
using AIPMS.Application.Features.Projects.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class RiskAlertWorkerTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Worker_disabled_flag_does_not_execute_sweep()
    {
        var services = new ServiceCollection();
        var probe = new MockRiskAlertService();
        services.AddSingleton<IRiskAlertService>(probe);
        var provider = services.BuildServiceProvider();

        var settings = new RiskAlertSettings { Enabled = false };
        var clock = new FixedTimeProvider(FixedNow);
        var worker = new RiskAlertWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            clock,
            NullLogger<RiskAlertWorker>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await worker.StartAsync(cts.Token);
        await Task.Delay(50, cts.Token);
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(0, probe.ProcessedCount);
    }

    [Fact]
    public async Task Worker_sweep_processes_batches_with_server_utc()
    {
        var services = new ServiceCollection();
        var probe = new MockRiskAlertService([101, 102]);
        services.AddSingleton<IRiskAlertService>(probe);
        var provider = services.BuildServiceProvider();

        var settings = new RiskAlertSettings { Enabled = true, BatchSize = 10 };
        var clock = new FixedTimeProvider(FixedNow);
        var worker = new RiskAlertWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings),
            clock,
            NullLogger<RiskAlertWorker>.Instance);

        await worker.SweepAsync(settings, CancellationToken.None);

        Assert.Equal(2, probe.ProcessedCount);
        Assert.Contains(101, probe.ProcessedProjectIds);
        Assert.Contains(102, probe.ProcessedProjectIds);
        Assert.All(probe.ProcessedTimes, time => Assert.Equal(FixedNow, time));
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class MockRiskAlertService(IReadOnlyList<long>? projectIds = null) : IRiskAlertService
    {
        private readonly IReadOnlyList<long> ids = projectIds ?? [];
        public int ProcessedCount { get; private set; }
        public List<long> ProcessedProjectIds { get; } = [];
        public List<DateTime> ProcessedTimes { get; } = [];

        public Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct)
        {
            var paged = ids.Where(id => id > afterId).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<long>>(paged);
        }

        public Task ProcessProjectAsync(long projectId, DateTime nowUtc, CancellationToken ct)
        {
            ProcessedCount++;
            ProcessedProjectIds.Add(projectId);
            ProcessedTimes.Add(nowUtc);
            return Task.CompletedTask;
        }
    }
}
