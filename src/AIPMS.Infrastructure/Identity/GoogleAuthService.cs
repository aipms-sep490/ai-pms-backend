using System.Security.Cryptography;
using System.Threading.Tasks;
using AIPMS.Application.Abstractions.Auditing;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Infrastructure.Identity.Configuration;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Generated.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace AIPMS.Infrastructure.Identity;

internal sealed class GoogleAuthService(AipmsDbContext db, IGoogleIdentityVerifier verifier,
    IOptions<GoogleAuthSettings> options, IOpaqueTokenService opaque, IPasswordHashingService passwords,
    IAccessTokenService accessTokens, IAccountSecurityPolicy policy, ICurrentUser current,
    IRequestContext request, IAuditTrail audit, TimeProvider clock) : IGoogleAuthService
{
    public async Task<GoogleChallenge> ChallengeAsync(string purpose, string browserBinding, CancellationToken ct)
    {
        Enabled();
        if (purpose is not ("LOGIN" or "LINK")) throw new ArgumentException("Purpose must be LOGIN or LINK.");
        var userId = purpose == "LINK" ? CurrentId() : (long?)null;
        if (userId is not null) EnsureAccount(await AccountAsync(userId.Value, ct));
        var nonce = opaque.Generate();
        var now = clock.GetUtcNow().UtcDateTime;
        var item = new ExternalLoginChallenge { Id = Guid.NewGuid(), Purpose = purpose, UserId = userId,
            NonceHash = nonce.Hash, BrowserHash = opaque.Hash(browserBinding), CreatedAt = now, ExpiresAt = now.AddMinutes(5) };
        db.ExternalLoginChallenges.Add(item);
        await db.SaveChangesAsync(ct);
        return new GoogleChallenge(item.Id, options.Value.ClientId, nonce.Value, item.ExpiresAt);
    }

    public async Task<LoginResponse> LoginAsync(Guid challengeId, string idToken, string browserBinding, CancellationToken ct)
    {
        Enabled();
        var identity = await VerifyAsync(idToken, "AUTH_GOOGLE_LOGIN", ct);
        return await TransactionAsync(async () =>
        {
            if (!await ConsumeAsync(challengeId, "LOGIN", null, identity.Nonce, browserBinding, ct))
                return await DeniedAsync<LoginResponse>(null, "AUTH_GOOGLE_LOGIN", Invalid(), ct);
            var link = await db.UserExternalLogins.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == "GOOGLE" && x.Subject == identity.Subject, ct);
            User? user = null;
            if (link is not null) user = await LockedAccountAsync(link.UserId, ct);
            // Re-read after the user lock: unlink may have completed while login was waiting.
            if (user is null || !await db.UserExternalLogins.AnyAsync(x => x.Id == link!.Id, ct))
                return await DeniedAsync<LoginResponse>(null, "AUTH_GOOGLE_LOGIN", Invalid(), ct);
            var denial = AccountDenial(user);
            if (denial is not null) return await DeniedAsync<LoginResponse>(user.Id, "AUTH_GOOGLE_LOGIN", denial, ct);
            if (!EmailMatches(user.Email, identity.Email)) return await DeniedAsync<LoginResponse>(user.Id, "AUTH_GOOGLE_LOGIN", Invalid(), ct);
            var now = clock.GetUtcNow().UtcDateTime;
            var roles = await db.UserRoles.Where(x => x.UserId == user.Id).Select(x => x.Role.Code).ToArrayAsync(ct);
            var access = accessTokens.Create(new AccessTokenDescriptor(user.Id, user.Email, user.FullName, roles, user.PasswordChangedAt));
            var refresh = opaque.Generate();
            var expires = now.AddDays(policy.RefreshTokenDays);
            db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, TokenHash = refresh.Hash, FamilyId = Guid.NewGuid(),
                ExpiresAt = expires, CreatedAt = now, CreatedByIp = request.IpAddress, UserAgent = request.UserAgent });
            user.LastLoginAt = now; user.UpdatedAt = now; user.AccessFailedCount = 0; user.LockoutEndAt = null;
            await AuditAsync(user.Id, "AUTH_GOOGLE_LOGIN", "SUCCESS", ct);
            return new Outcome<LoginResponse>(new LoginResponse(access.Token, "Bearer", access.ExpiresAtUtc,
                refresh.Value, expires, new AuthUserDto(user.Id, user.Email, user.FullName, roles)), null);
        }, ct);
    }

    public async Task LinkAsync(Guid challengeId, string idToken, string browserBinding, string currentPassword, CancellationToken ct)
    {
        Enabled();
        var userId = CurrentId();
        var identity = await VerifyAsync(idToken, "AUTH_GOOGLE_LINK", ct);
        await TransactionAsync(async () =>
        {
            if (!await ConsumeAsync(challengeId, "LINK", userId, identity.Nonce, browserBinding, ct))
                return await DeniedAsync<bool>(userId, "AUTH_GOOGLE_LINK", Invalid(), ct);
            var user = await LockedAccountAsync(userId, ct);
            var denial = CheckPassword(user, currentPassword);
            if (denial is not null) return await DeniedAsync<bool>(userId, "AUTH_GOOGLE_LINK", denial, ct);
            if (!EmailMatches(user.Email, identity.Email)) return await DeniedAsync<bool>(userId, "AUTH_GOOGLE_LINK", new ConflictException("Google email must match your AI-PMS email."), ct);
            var existing = await db.UserExternalLogins.SingleOrDefaultAsync(x => x.UserId == userId && x.Provider == "GOOGLE", ct);
            if (existing is not null && existing.Subject != identity.Subject
                || await db.UserExternalLogins.AnyAsync(x => x.Provider == "GOOGLE" && x.Subject == identity.Subject && x.UserId != userId, ct))
                return await DeniedAsync<bool>(userId, "AUTH_GOOGLE_LINK", new ConflictException("This Google identity cannot be linked."), ct);
            var now = clock.GetUtcNow().UtcDateTime;
            if (existing is null) db.UserExternalLogins.Add(new UserExternalLogin { UserId = userId,
                Subject = identity.Subject, Email = identity.Email, CreatedAt = now, UpdatedAt = now });
            user.AccessFailedCount = 0; user.LockoutEndAt = null;
            await AuditAsync(userId, "AUTH_GOOGLE_LINK", "SUCCESS", ct);
            return new Outcome<bool>(true, null);
        }, ct);
    }

    public async Task UnlinkAsync(string currentPassword, CancellationToken ct)
    {
        // Allow recovery even while Google login is disabled.
        var userId = CurrentId();
        await TransactionAsync(async () =>
        {
            var user = await LockedAccountAsync(userId, ct);
            var denial = CheckPassword(user, currentPassword);
            if (denial is not null) return await DeniedAsync<bool>(userId, "AUTH_GOOGLE_UNLINK", denial, ct);
            await db.UserExternalLogins.Where(x => x.UserId == userId && x.Provider == "GOOGLE").ExecuteDeleteAsync(ct);
            var now = clock.GetUtcNow().UtcDateTime;
            await db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokedByIp, request.IpAddress), ct);
            user.AccessFailedCount = 0; user.LockoutEndAt = null;
            await AuditAsync(userId, "AUTH_GOOGLE_UNLINK", "SUCCESS", ct);
            return new Outcome<bool>(true, null);
        }, ct);
    }

    public async Task<IReadOnlyList<ExternalLoginDto>> ListAsync(CancellationToken ct)
    {
        var userId = CurrentId();
        EnsureAccount(await AccountAsync(userId, ct));
        return await db.UserExternalLogins.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => new ExternalLoginDto(x.Provider, x.Email, x.CreatedAt)).ToArrayAsync(ct);
    }

    public Task CleanupAsync(CancellationToken ct) => db.ExternalLoginChallenges
        .Where(x => x.ExpiresAt < clock.GetUtcNow().UtcDateTime.AddDays(-1)).ExecuteDeleteAsync(ct);

    private async Task<bool> ConsumeAsync(Guid id, string purpose, long? userId, string nonce, string browser, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var nonceHash = opaque.Hash(nonce); var browserHash = opaque.Hash(browser);
        var changed = await db.ExternalLoginChallenges.Where(x => x.Id == id && x.Purpose == purpose && x.UserId == userId
                && x.ConsumedAt == null && x.ExpiresAt > now && x.NonceHash == nonceHash && x.BrowserHash == browserHash)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), ct);
        return changed == 1;
    }

    private async Task<GoogleIdentity> VerifyAsync(string token, string action, CancellationToken ct)
    {
        try { return await verifier.VerifyAsync(token, ct); }
        catch (UnauthorizedException) { await AuditAsync(null, action, "FAILURE", ct); throw; }
    }

    private async Task<T> TransactionAsync<T>(Func<Task<Outcome<T>>> work, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        Outcome<T> outcome;
        try
        {
            outcome = await work();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        { throw new ConflictException("This Google identity cannot be linked."); }
        if (outcome.Error is not null) throw outcome.Error;
        return outcome.Value!;
    }

    private async Task<User> LockedAccountAsync(long id, CancellationToken ct)
    {
        await db.Database.SqlQuery<long>($"SELECT id AS Value FROM dbo.users WITH (UPDLOCK, HOLDLOCK) WHERE id = {id}").ToListAsync(ct);
        return await AccountAsync(id, ct);
    }

    private Task<User> AccountAsync(long id, CancellationToken ct) => db.Users.SingleAsync(x => x.Id == id, ct);
    private Exception? AccountDenial(User user) => user.Status != "ACTIVE" ? new ForbiddenException("This account is not active.")
        : user.LockoutEndAt > clock.GetUtcNow().UtcDateTime ? Invalid() : null;
    private void EnsureAccount(User user) { var error = AccountDenial(user); if (error is not null) throw error; }
    private Exception? CheckPassword(User user, string password)
    {
        var denial = AccountDenial(user);
        if (denial is not null) return denial;
        if (passwords.Verify(user.PasswordHash, password)) return null;
        user.AccessFailedCount++;
        if (user.AccessFailedCount >= policy.FailedLoginThreshold) user.LockoutEndAt = clock.GetUtcNow().UtcDateTime.AddMinutes(policy.LockoutMinutes);
        user.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        return Invalid();
    }
    private async Task<Outcome<T>> DeniedAsync<T>(long? id, string action, Exception error, CancellationToken ct)
    { await AuditAsync(id, action, "DENIED", ct); return new Outcome<T>(default, error); }
    private Task AuditAsync(long? id, string action, string outcome, CancellationToken ct) => audit.RecordAsync(
        new AuditEntry(id, action, "USER", id, new Dictionary<string, object?> { ["provider"] = "GOOGLE" }, outcome), ct);
    private void Enabled() { if (!options.Value.Enabled) throw new ServiceUnavailableException("Google login is disabled."); }
    private long CurrentId() => current.IsAuthenticated && current.UserId is long id ? id : throw Invalid();
    private static bool EmailMatches(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static UnauthorizedException Invalid() => new("Google authentication failed. Sign in with your AI-PMS password to manage your link.");
    private sealed record Outcome<T>(T? Value, Exception? Error);
}
