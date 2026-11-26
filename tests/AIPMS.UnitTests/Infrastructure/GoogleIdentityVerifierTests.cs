using System.Security.Cryptography;
using AIPMS.Application.Common.Exceptions;
using AIPMS.Infrastructure.Identity;
using AIPMS.Infrastructure.Identity.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.UnitTests.Infrastructure;

public sealed class GoogleIdentityVerifierTests : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private const string Client = "test.apps.googleusercontent.com";
    private const string Issuer = "https://accounts.google.com";

    [Fact]
    public async Task Signed_identity_retains_subject_case_and_verified_nonce()
    {
        var result = await Verifier().VerifyAsync(Token(), default);
        Assert.Equal("CaseSensitiveSubject", result.Subject);
        Assert.Equal("student@gmail.com", result.Email);
        Assert.Equal("nonce", result.Nonce);
        Assert.True(result.IsAuthoritativeEmail);
    }

    [Theory]
    [InlineData("student@fpt.edu.vn", "fpt.edu.vn", true)]
    [InlineData("student@fpt.edu.vn", null, false)]
    [InlineData("student@fpt.edu.vn", "other.example", false)]
    public async Task Workspace_email_requires_matching_signed_hosted_domain(string email, string? hostedDomain, bool expected)
    {
        Assert.Equal(expected, (await Verifier().VerifyAsync(Token(email: email, hostedDomain: hostedDomain), default)).IsAuthoritativeEmail);
    }

    [Theory]
    [InlineData("aud")][InlineData("iss")][InlineData("expired")][InlineData("email_verified")]
    [InlineData("nonce")][InlineData("sub")][InlineData("email")][InlineData("iat")][InlineData("azp")]
    [InlineData("signature")][InlineData("unsigned")]
    public async Task Rejects_invalid_signed_claims_or_signature(string defect) =>
        await Assert.ThrowsAsync<UnauthorizedException>(() => Verifier().VerifyAsync(Token(defect), default));

    [Theory]
    [InlineData("")][InlineData("not-a-token")][InlineData("a.b.c")]
    public async Task Rejects_malformed_input(string value) =>
        await Assert.ThrowsAsync<UnauthorizedException>(() => Verifier().VerifyAsync(value, default));

    [Theory]
    [InlineData("https://app.example.com", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://app.example.com", false)]
    [InlineData("https://app.example.com/path", false)]
    [InlineData("*", false)]
    public void Configuration_requires_exact_secure_origins(string origin, bool valid) =>
        Assert.Equal(valid, new GoogleAuthSettings { Enabled = true, ClientId = Client, AllowedOrigins = [origin] }.IsValid());

    [Fact]
    public async Task Key_provider_failure_is_service_unavailable()
    {
        using var keys = new GoogleSigningKeys(new HttpClient(new FailureHandler()), TimeProvider.System);
        await Assert.ThrowsAsync<ServiceUnavailableException>(() => keys.GetAsync(false, default));
    }

    [Fact]
    public async Task Accepts_mobile_android_authorized_party_when_configured()
    {
        const string androidClient = "android-mobile.apps.googleusercontent.com";
        var verifier = new GoogleIdentityVerifier(
            new Keys(new RsaSecurityKey(rsa) { KeyId = "test" }),
            Options.Create(new GoogleAuthSettings
            {
                ClientId = Client,
                AllowedAuthorizedParties = [androidClient]
            }),
            TimeProvider.System);

        var result = await verifier.VerifyAsync(Token(authorizedParty: androidClient), default);
        Assert.Equal("CaseSensitiveSubject", result.Subject);
        Assert.Equal("student@gmail.com", result.Email);
    }

    [Fact]
    public void Configuration_validates_allowed_authorized_parties()
    {
        var valid = new GoogleAuthSettings
        {
            Enabled = true,
            ClientId = Client,
            AllowedOrigins = ["https://app.example.com"],
            AllowedAuthorizedParties = ["android-mobile.apps.googleusercontent.com"]
        };
        var invalid = new GoogleAuthSettings
        {
            Enabled = true,
            ClientId = Client,
            AllowedOrigins = ["https://app.example.com"],
            AllowedAuthorizedParties = ["not-a-google-client"]
        };
        Assert.True(valid.IsValid());
        Assert.False(invalid.IsValid());
    }

    private GoogleIdentityVerifier Verifier() => new(new Keys(new RsaSecurityKey(rsa) { KeyId = "test" }),
        Options.Create(new GoogleAuthSettings { ClientId = Client }), TimeProvider.System);

    private string Token(string? defect = null, string? email = null, string? hostedDomain = null, string? authorizedParty = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        { ["sub"] = "CaseSensitiveSubject", ["email"] = "student@gmail.com", ["email_verified"] = true, ["nonce"] = "nonce" };
        if (email is not null) claims["email"] = email;
        if (hostedDomain is not null) claims["hd"] = hostedDomain;
        if (authorizedParty is not null) claims["azp"] = authorizedParty;
        if (defect is "nonce" or "sub" or "email") claims.Remove(defect);
        if (defect == "email_verified") claims["email_verified"] = false;
        if (defect == "azp") claims["azp"] = "drive-client";
        using var other = RSA.Create(2048);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = defect == "iss" ? "https://attacker.example" : Issuer,
            Audience = defect == "aud" ? "drive-client" : Client,
            IssuedAt = defect == "iat" ? now.AddHours(1) : now.AddMinutes(-10), NotBefore = now.AddMinutes(-10),
            Expires = defect == "expired" ? now.AddMinutes(-2) : now.AddMinutes(10), Claims = claims,
            SigningCredentials = defect == "unsigned" ? null : new SigningCredentials(
                new RsaSecurityKey(defect == "signature" ? other : rsa) { KeyId = "test" }, SecurityAlgorithms.RsaSha256)
        });
    }
    public void Dispose() => rsa.Dispose();
    private sealed class Keys(SecurityKey key) : IGoogleSigningKeys
    { public Task<IReadOnlyCollection<SecurityKey>> GetAsync(bool refresh, CancellationToken ct) => Task.FromResult<IReadOnlyCollection<SecurityKey>>([key]); }
    private sealed class FailureHandler : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("Unavailable"); }
}
