using System.Data;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Security;
using AIPMS.Application.Features.Notifications.Abstractions;
using AIPMS.Application.Features.Notifications.DTOs;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;
using EmailDeliveryEntity = AIPMS.Infrastructure.Persistence.Models.NotificationEmailDelivery;

namespace AIPMS.Infrastructure.Persistence.Repositories;

internal sealed class NotificationEmailAdminService(
    AipmsDbContext db,
    ICurrentUser current,
    IAuditTrail audit,
    TimeProvider clock) : INotificationEmailAdminService
{
    public async Task<EmailDeliverySummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        EnsureAuthorized();

        var counts = await db.Set<EmailDeliveryEntity>().AsNoTracking()
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var oldestPending = await db.Set<EmailDeliveryEntity>().AsNoTracking()
            .Where(d => d.Status == "PENDING" || d.Status == "RETRY")
            .OrderBy(d => d.NextAttemptAt)
            .Select(d => (DateTime?)DateTime.SpecifyKind(d.NextAttemptAt, DateTimeKind.Utc))
            .FirstOrDefaultAsync(ct);

        var recentFailures = await db.Set<EmailDeliveryEntity>().AsNoTracking()
            .Where(d => d.Status == "FAILED" || (d.Status == "RETRY" && d.LastError != null))
            .OrderByDescending(d => d.LastAttemptAt ?? d.NextAttemptAt)
            .Take(20)
            .Select(d => new EmailDeliveryFailureDto(
                d.NotificationRecipientId,
                d.NotificationRecipient.NotificationId,
                d.NotificationRecipient.Notification.NotificationType,
                d.NotificationRecipient.User.Email,
                d.Status,
                d.AttemptCount,
                DateTime.SpecifyKind(d.NextAttemptAt, DateTimeKind.Utc),
                d.LastAttemptAt.HasValue ? DateTime.SpecifyKind(d.LastAttemptAt.Value, DateTimeKind.Utc) : null,
                d.LastError))
            .ToListAsync(ct);

        int CountOf(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return new EmailDeliverySummaryDto(
            CountOf("PENDING"),
            CountOf("RETRY"),
            CountOf("SENDING"),
            CountOf("SENT"),
            CountOf("FAILED"),
            oldestPending,
            recentFailures);
    }

    public async Task<RetryFailedEmailsResultDto> RetryFailedAsync(CancellationToken ct = default)
    {
        var actorId = EnsureAuthorized();
        var now = clock.GetUtcNow().UtcDateTime;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var failed = await db.Set<EmailDeliveryEntity>()
            .Where(d => d.Status == "FAILED")
            .ToListAsync(ct);

        foreach (var item in failed)
        {
            item.Status = "RETRY";
            item.AttemptCount = 0;
            item.NextAttemptAt = now;
            item.LastError = null;
        }

        if (failed.Count > 0)
            await db.SaveChangesAsync(ct);

        await audit.RecordAsync(new AuditEntry(
            actorId,
            "EMAIL_DELIVERIES_RETRY_REQUESTED",
            "NOTIFICATION_EMAIL_QUEUE",
            null,
            new Dictionary<string, object?> { ["requeuedCount"] = failed.Count }), ct);

        await tx.CommitAsync(ct);
        return new RetryFailedEmailsResultDto(failed.Count, now);
    }

    private long EnsureAuthorized()
    {
        if (!current.IsAuthenticated || current.UserId is not long actorId)
            throw new UnauthorizedException();
        if (!current.Roles.Contains(AppRoles.Admin, StringComparer.Ordinal)
            && !current.Roles.Contains(AppRoles.DepartmentStaff, StringComparer.Ordinal))
            throw new ForbiddenException("Only administrators or department staff can manage email delivery queues.");
        return actorId;
    }
}
