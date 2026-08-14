using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace AIPMS.IntegrationTests;

public sealed class ConfigurationValidationTests
{
    [Fact]
    public void Start_MissingDefaultConnection_FailsConfigurationValidation()
    {
        using var factory = new MissingDatabaseConnectionWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "DefaultConnection is not configured.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_InvalidCorsOrigin_FailsConfigurationValidation()
    {
        using var factory = new InvalidCorsWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Every CORS origin must be an absolute HTTP or HTTPS URL.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_MissingJwtSigningKey_FailsConfigurationValidation()
    {
        using var factory = new MissingJwtSigningKeyWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Jwt:SigningKey must contain at least 32 characters.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Start_PlaceholderJwtSigningKey_FailsConfigurationValidation()
    {
        using var factory = new PlaceholderJwtSigningKeyWebApplicationFactory();

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(
            "Jwt:SigningKey cannot use the placeholder value.",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    private sealed class InvalidCorsWebApplicationFactory : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Cors:AllowedOrigins:0"] = "not-an-origin"
                });
            });
        }
    }

    private sealed class MissingDatabaseConnectionWebApplicationFactory
        : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = string.Empty
                });
            });
        }
    }

    private sealed class MissingJwtSigningKeyWebApplicationFactory
        : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] = string.Empty
                });
            });
        }
    }

    private sealed class PlaceholderJwtSigningKeyWebApplicationFactory
        : AipmsWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:SigningKey"] =
                        "REPLACE_WITH_USER_SECRET_AT_LEAST_32_CHARACTERS"
                });
            });
        }
    }
}
