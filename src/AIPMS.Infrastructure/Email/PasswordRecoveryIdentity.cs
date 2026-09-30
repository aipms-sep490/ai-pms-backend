using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPMS.Infrastructure.Persistence.Generated;
using Microsoft.EntityFrameworkCore;

namespace AIPMS.Infrastructure.Email;

internal static class PasswordRecoveryIdentity
{
    public static string Fingerprint(string email, string key) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(email.Trim().ToUpperInvariant())));

    public static Task LockAsync(AipmsDbContext db, string hash, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock @Resource={"PasswordRecovery:" + hash}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
        IF @result < 0 THROW 51000, 'Password recovery lock unavailable.', 1;
        """, ct);
}
