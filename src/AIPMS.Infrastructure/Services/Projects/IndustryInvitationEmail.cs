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
            var expiresVn = DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc).AddHours(7).ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            return $"Bạn được mời tham gia hệ thống AI-PMS với vai trò chuyên gia cố vấn doanh nghiệp. Vui lòng đăng nhập bằng tài khoản Google gắn với địa chỉ email này.\n\n{settings.Value.InvitationUrl}#invitation={Uri.EscapeDataString(token)}\n\nHết hạn vào {expiresVn} (Việt Nam, UTC+7). Lời mời này chỉ sử dụng một lần. Nhận xét cố vấn mang tính tham khảo và không ảnh hưởng trực tiếp đến điểm học thuật.";
        }
        catch (CryptographicException) { return null; }
    }
}
