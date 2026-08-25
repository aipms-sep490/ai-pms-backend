using System.Security.Cryptography;
using System.Text;
using AIPMS.Application.Abstractions.Security;

namespace AIPMS.Infrastructure.Identity;

internal sealed class OpaqueTokenService : IOpaqueTokenService
{
    public OpaqueToken Generate()
    {
        var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return new OpaqueToken(value, Hash(value));
    }

    public byte[] Hash(string token) => SHA512.HashData(Encoding.UTF8.GetBytes(token));
}
