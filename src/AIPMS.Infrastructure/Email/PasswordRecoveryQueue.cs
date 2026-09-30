using System.Text.Json;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Email;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Email;

internal sealed class PasswordRecoveryQueue(
    AipmsDbContext db,
    IDataProtectionProvider protection,
    IOptions<PasswordRecoverySettings> options,
    IOpaqueTokenService tokens,
    IPasswordResetNotifier notifier,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<PasswordRecoveryQueue> logger) : IPasswordRecoveryQueue
{
    private readonly IDataProtector protector = protection.CreateProtector("AI-PMS.PasswordRecovery.Payload.v1");
    private readonly PasswordRecoverySettings settings = options.Value;
    private sealed record Payload(string Email, string? Token = null);

    public async Task EnqueueAsync(string email, CancellationToken ct)
    {
        if (!settings.Enabled || !settings.IsReady)
            throw new ServiceUnavailableException("Password recovery is currently unavailable.");

        var now = clock.GetUtcNow().UtcDateTime;
        var emailHash = Fingerprint(email);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await AcquireLockAsync(emailHash, ct);
            await db.PasswordRecoveryRequests
                .Where(x => x.EmailHash == emailHash && (x.Status == "PENDING" || x.Status == "SENDING" || x.Status == "RETRY" || x.Status == "SENT"))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SUPERSEDED")
                    .SetProperty(x => x.ProtectedPayload, (string?)null)
                    .SetProperty(x => x.CompletedAt, now)
                    .SetProperty(x => x.LeaseToken, (Guid?)null)
                    .SetProperty(x => x.LeaseUntil, (DateTime?)null), ct);
            var row = new PasswordRecoveryRequest
            {
                EmailHash = emailHash,
                ProtectedPayload = protector.Protect(JsonSerializer.Serialize(new Payload(email.Trim()))),
                Status = "PENDING",
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(30),
                NextAttemptAt = now
            };
            db.PasswordRecoveryRequests.Add(row);
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEntry(null, "AUTH_PASSWORD_RESET_REQUESTED", "PASSWORD_RECOVERY_REQUEST", row.Id,
                new Dictionary<string, object?>()), ct);
            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            logger.LogError("Password recovery enqueue failed; request was not accepted.");
            throw new ServiceUnavailableException("Password recovery is currently unavailable.");
        }
    }

    public async Task<bool> ProcessOneAsync(CancellationToken ct)
    {
        if (!settings.Enabled || !settings.IsReady) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        PasswordRecoveryRequest? row;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            row = await db.PasswordRecoveryRequests.FromSqlInterpolated($"""
                SELECT TOP (1) * FROM dbo.password_recovery_requests WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE ((status IN ('PENDING','RETRY') AND next_attempt_at <= {now})
                    OR (status = 'SENDING' AND lease_until <= {now}))
                ORDER BY next_attempt_at, id
                """).FirstOrDefaultAsync(ct);
            if (row is null) return false;
            row.Status = "SENDING";
            row.LeaseToken = Guid.NewGuid();
            row.LeaseUntil = now.AddSeconds(settings.LeaseSeconds);
            row.AttemptCount++;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var requestId = row.Id;
        var lease = row.LeaseToken!.Value;
        db.ChangeTracker.Clear();
        try
        {
            var prepared = await PrepareAsync(requestId, lease, ct);
            if (prepared is null) return true;
            await notifier.SendAsync(prepared.Value.Email, prepared.Value.Name, prepared.Value.Token, prepared.Value.Expires, ct);
            await FinishAsync(requestId, lease, "SENT", null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            var current = clock.GetUtcNow().UtcDateTime;
            var status = row.AttemptCount >= settings.MaxAttempts || row.ExpiresAt <= current ? "FAILED" : "RETRY";
            db.ChangeTracker.Clear();
            await Owned(requestId, lease).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
                .SetProperty(x => x.NextAttemptAt, current.Add(PasswordRecoverySettings.RetryDelay(row.AttemptCount)))
                .SetProperty(x => x.LeaseToken, (Guid?)null)
                .SetProperty(x => x.LeaseUntil, (DateTime?)null)
                .SetProperty(x => x.ProtectedPayload, x => status == "FAILED" ? null : x.ProtectedPayload)
                .SetProperty(x => x.CompletedAt, status == "FAILED" ? current : (DateTime?)null)
                .SetProperty(x => x.ErrorCode, status == "FAILED" ? "DELIVERY_FAILED" : "DELIVERY_RETRY"), ct);
            logger.LogWarning("Password recovery request {RequestId} failed; retry policy applied.", requestId);
        }
        return true;
    }

    private async Task<(string Email, string Name, string Token, DateTime Expires)?> PrepareAsync(long id, Guid lease, CancellationToken ct)
    {
        var snapshot = await Owned(id, lease).AsNoTracking().SingleOrDefaultAsync(ct);
        if (snapshot?.ProtectedPayload is null) return null;
        var payload = JsonSerializer.Deserialize<Payload>(protector.Unprotect(snapshot.ProtectedPayload))!;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK, HOLDLOCK) WHERE email = {payload.Email}").FirstOrDefaultAsync(ct);
        await AcquireLockAsync(snapshot.EmailHash, ct);
        var current = await db.PasswordRecoveryRequests.FromSqlInterpolated($"SELECT * FROM dbo.password_recovery_requests WITH (UPDLOCK, ROWLOCK) WHERE id = {id}").SingleAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        if (current.Status != "SENDING" || current.LeaseToken != lease || current.LeaseUntil <= now) return null;
        var invalid = current.ExpiresAt <= now ? "EXPIRED" : user is null || user.Status != "ACTIVE" ? "SKIPPED" : null;
        if (user?.PasswordRecoveryInvalidBefore >= current.CreatedAt) invalid = "SUPERSEDED";
        if (current.AttemptCount > settings.MaxAttempts) invalid = "FAILED";
        if (invalid is null && await db.PasswordRecoveryRequests.AnyAsync(x => x.EmailHash == current.EmailHash && x.Id > id, ct)) invalid = "SUPERSEDED";
        PasswordResetToken? reset = current.ResetTokenId.HasValue
            ? await db.PasswordResetTokens.SingleOrDefaultAsync(x => x.Id == current.ResetTokenId, ct) : null;
        if (current.ResetTokenId.HasValue && (reset is null || reset.UsedAt is not null || reset.ExpiresAt <= now)) invalid = "SUPERSEDED";
        if (invalid is not null)
        {
            current.Status = invalid; current.CompletedAt = now; current.ProtectedPayload = null; current.LeaseToken = null; current.LeaseUntil = null;
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return null;
        }
        string rawToken;
        if (reset is null)
        {
            await db.PasswordResetTokens.Where(x => x.UserId == user!.Id && x.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct);
            var token = tokens.Generate();
            reset = new PasswordResetToken { UserId = user!.Id, TokenHash = token.Hash, CreatedAt = now, ExpiresAt = current.ExpiresAt };
            db.PasswordResetTokens.Add(reset);
            await db.SaveChangesAsync(ct);
            rawToken = token.Value;
            current.ResetTokenId = reset.Id;
            current.ProtectedPayload = protector.Protect(JsonSerializer.Serialize(new Payload(payload.Email, rawToken)));
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new AuditEntry(user.Id, "AUTH_PASSWORD_RESET_TOKEN_CREATED", "PASSWORD_RECOVERY_REQUEST", id,
                new Dictionary<string, object?>()), ct);
        }
        else
        {
            rawToken = payload.Token ?? throw new InvalidOperationException("Recovery token payload is missing.");
        }
        await tx.CommitAsync(ct);
        return (user!.Email, user.FullName, rawToken, reset.ExpiresAt);
    }

    private IQueryable<PasswordRecoveryRequest> Owned(long id, Guid lease) => db.PasswordRecoveryRequests
        .Where(x => x.Id == id && x.Status == "SENDING" && x.LeaseToken == lease && x.LeaseUntil > clock.GetUtcNow().UtcDateTime);

    private Task<int> FinishAsync(long id, Guid lease, string status, string? error, CancellationToken ct) =>
        Owned(id, lease).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status)
            .SetProperty(x => x.CompletedAt, clock.GetUtcNow().UtcDateTime)
            .SetProperty(x => x.ProtectedPayload, (string?)null)
            .SetProperty(x => x.LeaseToken, (Guid?)null)
            .SetProperty(x => x.LeaseUntil, (DateTime?)null)
            .SetProperty(x => x.ErrorCode, error), ct);

    private Task AcquireLockAsync(string emailHash, CancellationToken ct) => PasswordRecoveryIdentity.LockAsync(db, emailHash, ct);

    private string Fingerprint(string email) => PasswordRecoveryIdentity.Fingerprint(email, settings.LookupKey);

    public Task CleanupAsync(CancellationToken ct) => db.PasswordRecoveryRequests
        .Where(x => x.CompletedAt < clock.GetUtcNow().UtcDateTime.AddDays(-7)).ExecuteDeleteAsync(ct);
}
