using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using AIPMS.Application.Common.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
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
                ["ConnectionStrings:DefaultConnection"] = "Server=(local);Database=AIPMS_Tests;",
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
            new(ClaimTypes.Name, fullName)
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
}
