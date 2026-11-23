using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIPMS.Application.Abstractions.Security;
using Microsoft.Extensions.Configuration;

namespace AIPMS.Infrastructure.Services.Files;

internal sealed class SignedPreviewTokenService(IConfiguration configuration, TimeProvider clock) : ISignedPreviewTokenService
{
    private readonly byte[] secret = Encoding.UTF8.GetBytes(
        (configuration["Jwt:SigningKey"] ?? "aipms-fallback-preview-signing-key-32bytes!!") + ":preview-stream-v1");

    public string Issue(SignedPreviewClaims claims)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(claims);
        var encodedPayload = Base64UrlEncode(payload);
        var sig = ComputeSignature(encodedPayload);
        return $"{encodedPayload}.{sig}";
    }

    public SignedPreviewClaims? Validate(string token, string expectedKind, long expectedResourceId, long expectedActorId)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        var expectedSig = ComputeSignature(parts[0]);
        if (!CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(parts[1]),
            Encoding.ASCII.GetBytes(expectedSig)))
            return null;

        try
        {
            var claims = JsonSerializer.Deserialize<SignedPreviewClaims>(Base64UrlDecode(parts[0]));
            if (claims is null) return null;
            if (!string.Equals(claims.ResourceKind, expectedKind, StringComparison.Ordinal)
                || claims.ResourceId != expectedResourceId
                || claims.ActorId != expectedActorId
                || claims.ExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
                return null;
            return claims;
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }
    }

    private string ComputeSignature(string encodedPayload)
    {
        using var hmac = new HMACSHA256(secret);
        return Base64UrlEncode(hmac.ComputeHash(Encoding.ASCII.GetBytes(encodedPayload)));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
