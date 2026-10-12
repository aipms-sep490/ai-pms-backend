using System.Net.Mail;
using System.Threading.Tasks;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Application.Common.Models;
using AIPMS.Application.Features.Auth.Abstractions;
using AIPMS.Application.Features.Auth.DTOs;
using AIPMS.Application.Features.Industry;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using M = AIPMS.Infrastructure.Persistence.Generated.Models;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed partial class IndustryService
{
    public async Task Cleanup(CancellationToken ct)
    {
        if (!settings.Value.Enabled) return;
        await db.Set<IndustryChallenge>().Where(c => c.ExpiresAt < Now.AddDays(-1)).ExecuteDeleteAsync(ct);
        await db.Set<IndustryInvitation>().Where(i => i.ProtectedToken != null && (i.ExpiresAt <= Now || i.ConsumedAt != null || i.RevokedAt != null))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ProtectedToken, (string?)null), ct);
    }
    private ExpertInvitationDto InvitationDto(IndustryInvitation row) => new(row.Id, row.ExpertId, row.Email,
        row.RevokedAt.HasValue ? "REVOKED" : row.ConsumedAt.HasValue ? "CONSUMED" : row.ExpiresAt <= Now ? "EXPIRED" : "PENDING", Utc(row.ExpiresAt));
    internal static bool ValidInvitationUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback) && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Query.Length == 0;
    public Task<ExpertInvitationDto> Invite(InviteExpertRequest input, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await Admin(ct);
        if (!google.Value.Enabled || !ValidInvitationUrl(settings.Value.InvitationUrl)) throw new ServiceUnavailableException("Configure Industry enrollment and Google before inviting experts.");
        var email = input.Email?.Trim().ToLowerInvariant() ?? "";
        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 255
            || string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Length > 255 || string.IsNullOrWhiteSpace(input.Organization) || input.Organization.Length > 255)
            throw Invalid("Valid email, full name and organization are required.");
        // Invitation cannot take over an existing academic identity. Reissue only for an unlinked expert created here.
        var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK,HOLDLOCK) WHERE email={email}").SingleOrDefaultAsync(ct);
        if (user is not null && (!await Expert(user.Id, false, ct) || user.GoogleEnrollmentPending
            || await db.UserExternalLogins.AnyAsync(l => l.UserId == user.Id, ct))) throw new ConflictException("Email belongs to an existing identity.");
        if (user is null)
        {
            user = new M.User { Email = email, FullName = input.FullName.Trim(), PasswordHash = passwords.Hash(opaque.Generate().Value),
                Status = "ACTIVE", CreatedAt = Now, UpdatedAt = Now, GoogleEnrollmentPending = false };
            db.Add(user); await db.SaveChangesAsync(ct);
            db.Add(new IndustryExpert { UserId = user.Id, Organization = input.Organization.Trim(), CreatedBy = Actor, CreatedAt = Now });
            await db.SaveChangesAsync(ct);
        }
        await db.Set<IndustryInvitation>().Where(i => i.ExpertId == user.Id && i.ConsumedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.RevokedAt, Now).SetProperty(i => i.ProtectedToken, (string?)null), ct);
        var token = opaque.Generate();
        var notification = new M.Notification { CreatedBy = Actor, NotificationType = NotificationType, Title = "AI-PMS expert invitation",
            Content = "An invitation was issued to your email address.", CreatedAt = Now, UpdatedAt = Now,
            RelatedEntityType = "USER", RelatedEntityId = user.Id, NotificationRecipients = [new M.NotificationRecipient {
                UserId = user.Id, DeliveredAt = Now, CreatedAt = Now, UpdatedAt = Now }] };
        db.Add(notification); await db.SaveChangesAsync(ct);
        db.Add(new NotificationEmailDelivery { NotificationRecipientId = notification.NotificationRecipients.Single().Id, NextAttemptAt = Now });
        var row = new IndustryInvitation { ExpertId = user.Id, Email = email, TokenHash = Convert.ToHexString(token.Hash),
            ProtectedToken = protection.CreateProtector(ProtectorName).Protect(token.Value), CreatedAt = Now, ExpiresAt = Now.AddDays(3), NotificationId = notification.Id };
        db.Add(row); await db.SaveChangesAsync(ct); await Audit("INDUSTRY_INVITED", "INDUSTRY_INVITATION", row.Id, null, ct);
        return InvitationDto(row);
    }, ct);
    public async Task<PagedResult<ExpertInvitationDto>> Invitations(int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await Admin(ct); var query = db.Set<IndustryInvitation>().AsNoTracking();
        var count = await query.CountAsync(ct); var rows = await query.OrderByDescending(i => i.Id).Skip((page-1)*pageSize).Take(pageSize).ToArrayAsync(ct);
        return new(rows.Select(InvitationDto).ToArray(), page, pageSize, count);
    }
    public Task<ExpertInvitationDto> RevokeInvitation(long id, CancellationToken ct) => transactions.InTransactionAsync(async () => {
        await Admin(ct);
        var row = await db.Set<IndustryInvitation>().FromSqlInterpolated($"SELECT * FROM dbo.industry_invitations WITH (UPDLOCK,HOLDLOCK) WHERE id={id}")
            .SingleOrDefaultAsync(ct) ?? throw new NotFoundException("Invitation", id);
        if (row.ConsumedAt.HasValue) throw new ConflictException("Enrollment already completed. Revoke assignments or disable the account instead.");
        row.RevokedAt ??= Now; row.ProtectedToken = null; await db.SaveChangesAsync(ct);
        await Audit("INDUSTRY_INVITATION_REVOKED", "INDUSTRY_INVITATION", id, null, ct); return InvitationDto(row);
    }, ct);
    private void GoogleEnabled() { Enabled(); if (!google.Value.Enabled) throw new ServiceUnavailableException("Google enrollment is disabled."); }
    public async Task<GoogleChallenge> Challenge(string browser, CancellationToken ct)
    {
        GoogleEnabled(); if (browser.Length != 64) throw new UnauthorizedException();
        var nonce = opaque.Generate();
        var row = new IndustryChallenge { Id = Guid.NewGuid(), NonceHash = Convert.ToHexString(nonce.Hash), BrowserHash = Convert.ToHexString(opaque.Hash(browser)), ExpiresAt = Now.AddMinutes(5) };
        db.Add(row); await db.SaveChangesAsync(ct); return new(row.Id, google.Value.ClientId, nonce.Value, Utc(row.ExpiresAt));
    }
    public async Task<LoginResponse> Enroll(IndustryEnrollmentInput input, string browser, CancellationToken ct)
    {
        GoogleEnabled();
        if (input.InvitationToken?.Length != 64 || browser.Length != 64) throw new UnauthorizedException("Invalid expert enrollment.");
        var identity = await verifier.VerifyAsync(input.IdToken, ct);
        var hash = Convert.ToHexString(opaque.Hash(input.InvitationToken)); var nonce = Convert.ToHexString(opaque.Hash(identity.Nonce)); var binding = Convert.ToHexString(opaque.Hash(browser));
        return await transactions.InTransactionAsync(async () => {
            var invite = await db.Set<IndustryInvitation>().FromSqlInterpolated($"SELECT * FROM dbo.industry_invitations WITH (UPDLOCK,HOLDLOCK) WHERE token_hash={hash}")
                .SingleOrDefaultAsync(ct);
            if (invite is null || invite.ConsumedAt.HasValue || invite.RevokedAt.HasValue || invite.ExpiresAt <= Now
                || !string.Equals(invite.Email, identity.Email.Trim(), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedException("Invalid expert enrollment.");
            var user = await db.Users.FromSqlInterpolated($"SELECT * FROM dbo.users WITH (UPDLOCK,HOLDLOCK) WHERE id={invite.ExpertId}").SingleAsync(ct);
            if (!await Expert(user.Id, false, ct) || user.LockoutEndAt > Now || user.GoogleEnrollmentPending
                || !string.Equals(user.Email, invite.Email, StringComparison.OrdinalIgnoreCase)) throw new ForbiddenException("Expert account is unavailable.");
            if (await db.UserExternalLogins.AnyAsync(l => l.Provider == "GOOGLE" && (l.UserId == user.Id || l.Subject == identity.Subject), ct))
                throw new ConflictException("Google identity already linked.");
            var used = await db.Set<IndustryChallenge>().Where(c => c.Id == input.ChallengeId && c.ConsumedAt == null && c.ExpiresAt > Now
                && c.NonceHash == nonce && c.BrowserHash == binding).ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, Now), ct);
            if (used != 1) throw new UnauthorizedException("Invalid expert enrollment challenge.");
            // The secret sent only to the invited mailbox supplies ownership proof for non-authoritative Google domains.
            invite.ConsumedAt = Now; invite.ProtectedToken = null;
            var expert = await db.Set<IndustryExpert>().SingleAsync(e => e.UserId == user.Id, ct); expert.EnrolledAt = Now;
            db.Add(new M.UserExternalLogin { UserId = user.Id, Provider = "GOOGLE", Subject = identity.Subject, Email = identity.Email, CreatedAt = Now, UpdatedAt = Now });
            var token = accessTokens.Create(new(user.Id, user.Email, user.FullName, [], user.PasswordChangedAt));
            var refresh = opaque.Generate(); var expires = Now.AddDays(accountPolicy.RefreshTokenDays);
            db.Add(new M.RefreshToken { UserId = user.Id, TokenHash = refresh.Hash, FamilyId = Guid.NewGuid(), CreatedAt = Now, ExpiresAt = expires,
                CreatedByIp = request.IpAddress, UserAgent = request.UserAgent });
            user.LastLoginAt = Now; user.UpdatedAt = Now; user.AccessFailedCount = 0; user.LockoutEndAt = null;
            await db.SaveChangesAsync(ct);
            await audit.RecordAsync(new(user.Id, "INDUSTRY_GOOGLE_ENROLLED", "INDUSTRY_INVITATION", invite.Id,
                new Dictionary<string, object?> { ["mailboxProof"] = "INVITATION", ["provider"] = "GOOGLE" }), ct);
            return new LoginResponse(token.Token, "Bearer", token.ExpiresAtUtc, refresh.Value, expires, new AuthUserDto(user.Id, user.Email, user.FullName, []));
        }, ct);
    }
}
