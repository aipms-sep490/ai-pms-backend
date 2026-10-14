using System.Security.Cryptography;
using System.Threading.Tasks;
using AIPMS.Application.Features.Industry;
using AIPMS.Infrastructure.Persistence.Generated;
using AIPMS.Infrastructure.Persistence.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIPMS.Infrastructure.Services.Projects;

internal sealed class IndustryInvitationEmail(AipmsDbContext db, IDataProtectionProvider protection, IOptions<IndustrySettings> settings)
{
    internal async Task<string?> Body(long notificationId, long userId, DateTime now, CancellationToken ct)
    {
        if (!settings.Value.Enabled || !IndustryService.ValidInvitationUrl(settings.Value.InvitationUrl)) return null;
        var row = await db.Set<IndustryInvitation>().AsNoTracking().SingleOrDefaultAsync(i => i.NotificationId == notificationId && i.ExpertId == userId
            && i.ConsumedAt == null && i.RevokedAt == null && i.ExpiresAt > now, ct);
        if (row?.ProtectedToken is null || !await db.Users.AnyAsync(u => u.Id == userId && u.Status == "ACTIVE" && u.Email == row.Email, ct)
            || await db.UserRoles.AnyAsync(r => r.UserId == userId, ct)) return null;
        try {
            var token = protection.CreateProtector(IndustryService.ProtectorName).Unprotect(row.ProtectedToken);
            // Fragment tokens are not sent to the web server in URL request logs. FE exchanges via JSON.
            return $"You are invited to AI-PMS as an industry advisor. Sign in with the Google account for this email.\n\n{settings.Value.InvitationUrl}#invitation={Uri.EscapeDataString(token)}\n\nExpires {row.ExpiresAt:yyyy-MM-dd HH:mm} UTC. This invitation is single-use. Advisory feedback does not affect academic scores.";
        }
        catch (CryptographicException) { return null; }
    }
}
