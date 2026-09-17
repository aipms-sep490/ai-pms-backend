using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class ScheduledNotificationWorkerTests
{
    [Fact]
    public async Task Worker_pages_past_failed_projects_with_fresh_scopes_and_server_UTC()
    {
        var state = new SweepState();
        using var app = new WorkerFactory(state);
        using var client = app.CreateClient();
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new long[] { 1, 2, 3 }, state.Processed.Select(p => p.Id));
        Assert.Equal(3, state.Processed.Select(p => p.Scope).Distinct().Count());
        Assert.Equal(new long[] { 0, 2, 3 }, state.Cursors);
        Assert.All(state.Processed, p => Assert.Equal(new DateTime(2026, 9, 12, 10, 0, 0, DateTimeKind.Utc), p.Now));
    }

    [Fact]
    public async Task Host_shutdown_cancels_in_flight_sweep()
    {
        var state = new SweepState { WaitForCancellation = true };
        var app = new WorkerFactory(state);
        using var client = app.CreateClient();
        await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await app.DisposeAsync();
        await state.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(state.Processed);
    }

    private sealed class SweepState
    {
        public bool WaitForCancellation { get; init; }
        public List<(long Id, Guid Scope, DateTime Now)> Processed { get; } = [];
        public List<long> Cursors { get; } = [];
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Probe(SweepState state) : IScheduledNotificationService
    {
        private readonly Guid scope = Guid.NewGuid();
        public Task<IReadOnlyList<long>> GetProjectIdsAsync(long afterId, int limit, CancellationToken ct)
        {
            Assert.Equal(2, limit);
            state.Cursors.Add(afterId);
            IReadOnlyList<long> ids = afterId switch { 0 => [1, 2], 2 => [3], _ => [] };
            if (ids.Count == 0) state.Completed.TrySetResult();
            return Task.FromResult(ids);
        }

        public async Task ProcessProjectAsync(long id, DateTime now, TimeSpan window, CancellationToken ct)
        {
            Assert.Equal(TimeSpan.FromHours(24), window);
            state.Started.TrySetResult();
            if (state.WaitForCancellation)
            {
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException) { state.Cancelled.TrySetResult(); throw; }
            }
            state.Processed.Add((id, scope, now));
            if (id == 1) throw new InvalidOperationException("Injected project failure");
        }
    }

    private sealed class WorkerFactory(SweepState state) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ScheduledNotifications:Enabled"] = "true", ["ScheduledNotifications:BatchSize"] = "2"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IScheduledNotificationService>();
                services.AddScoped<IScheduledNotificationService>(_ => new Probe(state));
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new Clock());
            });
        }
    }

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    }
}
