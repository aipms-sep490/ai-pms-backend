using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AIPMS.Application.Abstractions.Security;
using AIPMS.Application.Common.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AIPMS.IntegrationTests;

public class AipmsWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestIssuer = "AI-PMS.IntegrationTests";
    private const string TestAudience = "AI-PMS.IntegrationTests.Client";
    private const string TestSigningKey =
        "aipms-integration-tests-signing-key-64-characters-minimum-value";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    Environment.GetEnvironmentVariable("AIPMS_TEST_CONNECTION")
                    ?? "Server=(local);Database=AIPMS_Tests;Encrypt=False;TrustServerCertificate=True;",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Jwt:Issuer"] = TestIssuer,
                ["Jwt:Audience"] = TestAudience,
                ["Jwt:SigningKey"] = TestSigningKey,
                ["Jwt:AccessTokenMinutes"] = "60",
                ["Observability:LogFilePath"] = Path.Combine(
                    Path.GetTempPath(),
                    "aipms-tests-.log")
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccessTokenAccountValidator>();
            services.AddSingleton<IAccessTokenAccountValidator, AllowAccessTokenAccountValidator>();
        });
    }

    public HttpClient CreateAuthenticatedClient(
        long userId = 1001,
        string email = "student@aipms.test",
        string fullName = "Integration Test Student",
        params string[] roles)
    {
        var effectiveRoles = roles.Length == 0 ? [AppRoles.Student] : roles;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, fullName),
            new("pwd", "0")
        };

        claims.AddRange(effectiveRoles.Select(static role => new Claim(ClaimTypes.Role, role)));

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = TestIssuer,
            Audience = TestAudience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now,
            IssuedAt = now,
            Expires = now.AddMinutes(30),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            new JsonWebTokenHandler().CreateToken(descriptor));

        return client;
    }

    private sealed class AllowAccessTokenAccountValidator : IAccessTokenAccountValidator
    {
        public Task<bool> IsValidAsync(
            long userId,
            DateTime? passwordChangedAtUtc,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
