using AIPMS.Application.Features.Notifications.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class MeetingReminderWorkerTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Worker_pages_isolates_failures_skips_obsolete_and_retries_SMTP_with_attempt_fence()
    {
        var state = new State();
        await using var app = new Factory(state);
        using var client = app.CreateClient();
        await state.Completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(new long[] { 1, 2, 3 }, state.Enqueued.Select(x => x.Id));
        Assert.Equal(3, state.Enqueued.Select(x => x.Scope).Distinct().Count());
        Assert.Equal(new long[] { 0, 2, 3 }, state.Cursors);
        Assert.Equal(new long[] { 2, 3, 4 }, state.Sent);
        Assert.Equal(new[] { (3L, 1) }, state.MarkedSent);
        Assert.Equal(new[] { (2L, false, Now.AddSeconds(30), 1), (4L, true, Now.AddSeconds(240), 5), (5L, true, Now, 6) }, state.Failures);
    }

    [Fact]
    public void Enabling_reminders_without_SMTP_fails_startup()
    {
        using var app = new Factory(new State(), missingSmtp: true);
        var failure = Assert.ThrowsAny<Exception>(() => app.CreateClient());
        Assert.Contains("SMTP requires", failure.ToString());
    }

    private sealed class State
    {
        public List<long> Cursors { get; } = [];
        public List<(long Id, Guid Scope)> Enqueued { get; } = [];
        public List<long> Sent { get; } = [];
        public List<(long Id, int Attempt)> MarkedSent { get; } = [];
        public List<(long Id, bool Permanent, DateTime Next, int Attempt)> Failures { get; } = [];
        public Queue<NotificationEmailDelivery> Queue { get; } = new(new[]
        {
            new NotificationEmailDelivery(1, 1, "nobody@example.test", "Test", "Reminder", "Body", false),
            new NotificationEmailDelivery(2, 1, "nobody@example.test", "Test", "Reminder", "Body"),
            new NotificationEmailDelivery(3, 1, "nobody@example.test", "Test", "Reminder", "Body"),
            new NotificationEmailDelivery(4, 5, "nobody@example.test", "Test", "Reminder", "Body"),
            new NotificationEmailDelivery(5, 6, "nobody@example.test", "Test", "Reminder", "Body")
        });
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Probe(State state) : IMeetingReminderService, INotificationEmailQueue, INotificationEmailSender
    {
        private readonly Guid scope = Guid.NewGuid();
        public Task<IReadOnlyList<long>> GetDueMeetingIdsAsync(long afterId, int limit, DateTime nowUtc, int minutesBefore, CancellationToken ct)
        {
            Assert.Equal(Now, nowUtc);
            Assert.Equal(15, minutesBefore);
            state.Cursors.Add(afterId);
            return Task.FromResult<IReadOnlyList<long>>(afterId switch { 0 => [1, 2], 2 => [3], _ => [] });
        }
        public Task EnqueueAsync(long meetingId, DateTime nowUtc, int minutesBefore, CancellationToken ct)
        {
            state.Enqueued.Add((meetingId, scope));
            if (meetingId == 1) throw new InvalidOperationException("Injected per-meeting failure");
            return Task.CompletedTask;
        }
        public Task<NotificationEmailDelivery?> ClaimNextAsync(DateTime nowUtc, CancellationToken ct, string? notificationType = null)
        {
            Assert.Equal(IMeetingReminderService.NotificationType, notificationType);
            if (state.Queue.TryDequeue(out var item)) return Task.FromResult<NotificationEmailDelivery?>(item);
            state.Completed.TrySetResult();
            return Task.FromResult<NotificationEmailDelivery?>(null);
        }
        public Task<bool> TrySendAsync(NotificationEmailDelivery delivery, CancellationToken ct)
        {
            state.Sent.Add(delivery.RecipientId);
            return Task.FromResult(delivery.RecipientId == 3);
        }
        public Task MarkSentAsync(long recipientId, DateTime nowUtc, CancellationToken ct, int? expectedAttempt = null)
        {
            state.MarkedSent.Add((recipientId, expectedAttempt!.Value));
            return Task.CompletedTask;
        }
        public Task MarkFailedAsync(long recipientId, DateTime nextAttemptAtUtc, string error, bool permanent, CancellationToken ct, int? expectedAttempt = null)
        {
            state.Failures.Add((recipientId, permanent, nextAttemptAtUtc, expectedAttempt!.Value));
            return Task.CompletedTask;
        }
    }

    private sealed class Factory(State state, bool missingSmtp = false) : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MeetingReminders:Enabled"] = "true", ["MeetingReminders:BatchSize"] = "50",
                ["Email:Host"] = missingSmtp ? "" : "smtp.example.test", ["Email:SenderAddress"] = missingSmtp ? "" : "sender@example.test",
                ["Email:Username"] = missingSmtp ? "" : "sender", ["Email:Password"] = missingSmtp ? "" : "fake-test-only",
                ["Email:EnableSsl"] = "true", ["FileStorage:Provider"] = "Local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMeetingReminderService>();
                services.RemoveAll<INotificationEmailQueue>();
                services.RemoveAll<INotificationEmailSender>();
                services.RemoveAll<TimeProvider>();
                services.AddScoped<IMeetingReminderService>(_ => new Probe(state));
                services.AddScoped<INotificationEmailQueue>(_ => new Probe(state));
                services.AddScoped<INotificationEmailSender>(_ => new Probe(state));
                services.AddSingleton<TimeProvider>(new Clock());
            });
        }
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
