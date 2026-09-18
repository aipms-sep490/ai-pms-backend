using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using AIPMS.Infrastructure.Persistence.Repositories;
using AIPMS.IntegrationTests.FinalSubmissions;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.IntegrationTests.Notifications;

public sealed class NotificationEmailQueueTests(FinalSubmissionDraftDatabaseFixture fixture)
    : IClassFixture<FinalSubmissionDraftDatabaseFixture>
{
    private static readonly DateTime Now = FinalSubmissionDraftDatabaseFixture.Now;

    [Fact]
    public async Task Claim_is_exclusive_and_failed_delivery_is_retryable()
    {
        var scenario = await fixture.Seed();
        long recipientId;
        await using (var db = fixture.CreateContext())
        {
            var notification = new M.Notification { NotificationType = "TASK_OVERDUE", Title = "A task is overdue",
                Content = "A task is overdue.", CreatedAt = Now, UpdatedAt = Now,
                NotificationRecipients = [new() { UserId = scenario.Users.Student, CreatedAt = Now, UpdatedAt = Now }] };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            recipientId = notification.NotificationRecipients.Single().Id;
            db.Set<NotificationEmailDelivery>().Add(new() { NotificationRecipientId = recipientId, NextAttemptAt = Now });
            await db.SaveChangesAsync();
        }

        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = new NotificationEmailQueue(firstDb);
        var second = new NotificationEmailQueue(secondDb);
        var claimed = await Task.WhenAll(first.ClaimNextAsync(Now, default), second.ClaimNextAsync(Now, default));
        Assert.Single(claimed.Where(x => x is not null));
        var delivery = claimed.Single(x => x is not null)!;
        var claimer = claimed[0] is not null ? first : second;
        var other = claimed[0] is not null ? second : first;
        Assert.Equal(recipientId, delivery.RecipientId);
        Assert.Equal(1, delivery.AttemptCount);

        await claimer.MarkFailedAsync(recipientId, Now.AddMinutes(1), "temporary SMTP outage", false, default);
        await using (var verify = fixture.CreateContext())
        {
            var row = await verify.Set<NotificationEmailDelivery>().SingleAsync(x => x.NotificationRecipientId == recipientId);
            Assert.Equal("RETRY", row.Status);
            Assert.Equal("temporary SMTP outage", row.LastError);
            Assert.Equal(Now.AddMinutes(1), row.NextAttemptAt);
        }
        var retry = await other.ClaimNextAsync(Now.AddMinutes(1), default);
        Assert.NotNull(retry);
        await other.MarkSentAsync(recipientId, Now.AddMinutes(1), default);
        await using var sentDb = fixture.CreateContext();
        var sent = await sentDb.Set<NotificationEmailDelivery>().SingleAsync(x => x.NotificationRecipientId == recipientId);
        Assert.Equal("SENT", sent.Status);
        Assert.Equal(Now.AddMinutes(1), sent.SentAt);
    }

    [Fact]
    public async Task Permanent_failure_is_terminal_and_does_not_change_in_app_delivery()
    {
        var scenario = await fixture.Seed();
        await using (var db = fixture.CreateContext())
        {
            var notification = new M.Notification { NotificationType = "TASK_OVERDUE", Title = "Overdue",
                Content = "Overdue.", CreatedAt = Now, UpdatedAt = Now,
                NotificationRecipients = [new() { UserId = scenario.Users.Student, CreatedAt = Now, UpdatedAt = Now, DeliveredAt = Now }] };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            db.Set<NotificationEmailDelivery>().Add(new() { NotificationRecipientId = notification.NotificationRecipients.Single().Id, NextAttemptAt = Now });
            await db.SaveChangesAsync();
        }
        await using var db2 = fixture.CreateContext();
        var queue = new NotificationEmailQueue(db2);
        var delivery = (await queue.ClaimNextAsync(Now, default))!;
        await queue.MarkFailedAsync(delivery.RecipientId, Now.AddHours(1), "permanent failure", true, default);
        await using var stateDb = fixture.CreateContext();
        Assert.Equal("FAILED", await stateDb.Set<NotificationEmailDelivery>()
            .Where(x => x.NotificationRecipientId == delivery.RecipientId).Select(x => x.Status).SingleAsync());
        await using var verify = fixture.CreateContext();
        var recipient = await verify.NotificationRecipients.SingleAsync(r => r.Id == delivery.RecipientId);
        Assert.False(recipient.IsRead);
        Assert.NotNull(recipient.DeliveredAt);
    }
}
